using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("生产用料清单（PRD_PPBOM）位号回填服务插件")]
    public class PPBomDesignatorServicePlugIn : AbstractOperationServicePlugIn
    {
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);
            e.FieldKeys.Add("FMaterialID");       // 物料 ID
            e.FieldKeys.Add("FBOMID");            // 用料 BOM ID
            e.FieldKeys.Add("FMoId");             // 生产订单 ID
            e.FieldKeys.Add("FMoEntryId");        // 生产订单分录 ID
            e.FieldKeys.Add("F_BHD_Designator");  // 自定义位号 Key
        }

        public override void BeginOperationTransaction(BeginOperationTransactionArgs e)
        {
            base.BeginOperationTransaction(e);

            if (e.DataEntitys == null || !e.DataEntitys.Any()) return;

            foreach (DynamicObject dataObj in e.DataEntitys)
            {
                DynamicObjectCollection entryRows = dataObj["PPBOMENTRY"] as DynamicObjectCollection;
                if (entryRows == null) continue;

                foreach (DynamicObject row in entryRows)
                {
                    // 1. 获取子物料 ID (数据中的 MaterialID_Id)
                    long materialId = GetValueLong(row, "MaterialID_Id", "MaterialID");
                    if (materialId == 0) continue;

                    // 2. 获取用料行绑定的 BOMID，若为 0 则从生产订单（MoEntryId）追溯生产 BOM
                    long bomId = GetValueLong(row, "BOMID_Id", "BOMID");
                    long moEntryId = GetValueLong(row, "MoEntryId");

                    if (bomId == 0 && moEntryId > 0)
                    {
                        bomId = GetBomIdFromMoEntry(moEntryId);
                    }

                    // 3. 若仍未找到 BOMID，兜底获取该物料自身生效的默认 BOMID
                    if (bomId == 0)
                    {
                        bomId = GetDefaultBomId(materialId);
                    }

                    // 4. 从工程 BOM 的 T_ENG_BOMCHILD 子项明细表查询对应的位号
                    if (bomId > 0)
                    {
                        HashSet<string> designatorSet = new HashSet<string>();
                        HashSet<long> visitedBomIds = new HashSet<long>();

                        GetDesignatorsFromBomChild(bomId, materialId, designatorSet, visitedBomIds);

                        // 5. 将解析出的位号回写到生产用料清单的表体字段中
                        if (designatorSet.Count > 0)
                        {
                            row["F_BHD_Designator"] = string.Join(",", designatorSet);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 从生产订单明细（T_PRD_MOENTRY）中获取生产下达时选择的 BOMID
        /// </summary>
        private long GetBomIdFromMoEntry(long moEntryId)
        {
            string sql = $"SELECT FBOMID FROM T_PRD_MOENTRY WHERE FENTRYID = {moEntryId}";
            return DBUtils.ExecuteScalar<long>(this.Context, sql, 0, null);
        }

        /// <summary>
        /// 使用 ExecuteDynamicObject 一次性加载数据，避免 DataReader 冲突
        /// </summary>
        private void GetDesignatorsFromBomChild(long bomId, long targetMaterialId, HashSet<string> designatorSet, HashSet<long> visitedBomIds)
        {
            if (bomId <= 0 || visitedBomIds.Contains(bomId)) return;
            visitedBomIds.Add(bomId);

            string sql = $@"
                SELECT 
                    C.FMATERIALID AS CHILD_MATERIALID,
                    C.FBOMID AS CHILD_BOMID,
                    C.F_BHD_DESIGNATOR
                FROM T_ENG_BOM B
                INNER JOIN T_ENG_BOMCHILD C ON B.FID = C.FID
                WHERE B.FID = {bomId}";

            // 改用 ExecuteDynamicObject，直接将数据库查询结果加载至内存集合并快速关闭连接
            DynamicObjectCollection rows = DBUtils.ExecuteDynamicObject(this.Context, sql);
            if (rows == null || rows.Count == 0) return;

            List<long> nextLevelBomIds = new List<long>();

            foreach (DynamicObject row in rows)
            {
                long childMaterialId = Convert.ToInt64(row["CHILD_MATERIALID"]);
                string designator = row["F_BHD_DESIGNATOR"] != null ? row["F_BHD_DESIGNATOR"].ToString().Trim() : "";

                // 1. 匹配目标物料并提取位号
                if (childMaterialId == targetMaterialId && !string.IsNullOrEmpty(designator))
                {
                    string[] parts = designator.Split(new char[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        designatorSet.Add(part.Trim());
                    }
                }

                // 2. 收集深层子 BOM ID
                long childBomId = Convert.ToInt64(row["CHILD_BOMID"]);
                if (childBomId == 0 && childMaterialId > 0)
                {
                    childBomId = GetDefaultBomId(childMaterialId);
                }

                if (childBomId > 0)
                {
                    nextLevelBomIds.Add(childBomId);
                }
            }

            // 3. 递归展开下级 BOM（此时前面的数据库查询已完全结束，连接自由）
            foreach (long childBomId in nextLevelBomIds)
            {
                GetDesignatorsFromBomChild(childBomId, targetMaterialId, designatorSet, visitedBomIds);
            }
        }

        /// <summary>
        /// 查询物料当前生效的默认 BOM ID
        /// </summary>
        private long GetDefaultBomId(long materialId)
        {
            string sql = $@"
                SELECT TOP 1 FID 
                FROM T_ENG_BOM 
                WHERE FMATERIALID = {materialId} 
                  AND FDOCUMENTSTATUS = 'C' 
                  AND FFORBIDSTATUS = 'A'
                ORDER BY FISDEFAULT DESC, FID DESC";

            return DBUtils.ExecuteScalar<long>(this.Context, sql, 0, null);
        }

        /// <summary>
        /// 兼容旧版 C# 编译器的类型取值辅助函数
        /// </summary>
        private long GetValueLong(DynamicObject row, params string[] propertyNames)
        {
            if (row == null) return 0;
            foreach (var prop in propertyNames)
            {
                if (row.DynamicObjectType.Properties.Contains(prop))
                {
                    object val = row[prop];

                    DynamicObject dyObj = val as DynamicObject;
                    if (dyObj != null)
                    {
                        if (dyObj.DynamicObjectType.Properties.Contains("Id"))
                        {
                            long idVal = Convert.ToInt64(dyObj["Id"]);
                            if (idVal > 0) return idVal;
                        }
                    }
                    else if (val != null && val != DBNull.Value)
                    {
                        long result = 0;
                        if (long.TryParse(val.ToString(), out result) && result > 0)
                        {
                            return result;
                        }
                    }
                }
            }
            return 0;
        }
    }
}