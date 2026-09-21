using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Log;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using System.ComponentModel;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产用料清单（PRD_PPBOM）位号回填服务插件。
    ///
    /// 功能：
    ///   生产订单按 BOM 展开生成生产用料清单时，依据用料清单表头的 BOM 版本（FBOMID），
    ///   把该 BOM 子项（物料清单）上对应物料的位号 F_BHD_Designator 回填到用料清单子项上。
    ///
    /// 注册位置：
    ///   生产用料清单 → 保存（Save）操作。
    ///   若你的环境在【审核】时才落用料清单，请同时把本插件注册到 Audit 操作（逻辑幂等，重复执行无副作用）。
    ///
    /// 匹配策略（见 DesignatorHelper 说明）：
    ///   1) 用料清单子项 FBOMENTRYID == BOM 子项 FENTRYID —— 精确匹配，规避同料号多行串位号
    ///   2) (表头 FBOMID, 子项 FMATERIALID) 组合键 —— 兜底
    ///
    /// 幂等性：
    ///   默认只填充空白位号，不覆盖已有值（OverwriteExisting = false）。
    ///   因此本插件可安全注册到多个操作，也可重复执行。
    /// </summary>
    [HotUpdate]
    [Description("生产用料清单（PRD_PPBOM）位号回填服务插件")]
    public class PPBomDesignatorServicePlugIn : AbstractOperationServicePlugIn
    {
        /// <summary>
        /// 是否覆盖用料清单上已有的位号。
        /// false（默认）：只补空白，人工维护过的位号不会被冲掉。
        /// true：一律以 BOM 为准重写。
        /// </summary>
        private const bool OverwriteExisting = false;

        /// <summary>
        /// 回填失败时是否中断保存。
        /// false（默认）：仅记录日志。位号属辅助工艺信息，不阻断业务单据保存。
        /// 若车间强依赖位号、缺失会导致错料，建议改为 true 并配合下面的严格校验。
        /// </summary>
        private const bool ThrowOnFailure = false;

        /// <summary>
        /// 保存后是否校验"凡是 BOM 有位号的子项，用料清单都必须有位号"。
        /// 打开后若有漏填会记 Error 级日志（ThrowOnFailure = true 时直接抛异常）。
        /// </summary>
        private const bool ValidateAfterFill = true;

        /// <summary>声明需要加载的字段。不声明则取值恒为 null。</summary>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);

            e.FieldKeys.Add("FBomId");              // 表头：BOM 版本
            e.FieldKeys.Add("FMaterialId");         // 子项：子项物料
            e.FieldKeys.Add("FBOMENTRYID");         // 子项：BOM 分录内码（精确匹配键）
            e.FieldKeys.Add(DesignatorHelper.DesignatorField); // 子项：位号
        }

        /// <summary>
        /// 事务内处理：标准逻辑已把用料清单数据装配完毕，此处补位号后随保存一并落库。
        /// 查询走 DBUtils（事务内允许），不可使用 ORM 查询服务，否则报"事务中不允许查询"。
        /// </summary>
        public override void BeginOperationTransaction(BeginOperationTransactionArgs e)
        {
            base.BeginOperationTransaction(e);

            if (e.DataEntitys == null || e.DataEntitys.Length == 0)
            {
                return;
            }

            try
            {
                FillDesignators(e.DataEntitys);
            }
            catch (Exception ex)
            {
                // 位号回填属增强逻辑，默认不阻断保存，仅记录以便排查。
                Logger.Error(DesignatorHelper.LogTitle, "位号回填异常：" + ex.Message, ex);

                if (ThrowOnFailure)
                {
                    throw new KDBusinessException("BHD_PPBOM_DESIGNATOR", "位号回填失败：" + ex.Message);
                }
            }
        }

        /// <summary>核心回填逻辑。</summary>
        private void FillDesignators(IEnumerable<DynamicObject> bills)
        {
            var billList = bills.Where(b => b != null).ToList();
            if (billList.Count == 0)
            {
                return;
            }

            // 1. 收集本次涉及的全部 BOM 版本内码
            var bomIds = new List<long>();
            foreach (DynamicObject bill in billList)
            {
                long bomId = DesignatorHelper.GetBomId(bill);
                if (bomId > 0L)
                {
                    bomIds.Add(bomId);
                }
            }

            if (bomIds.Count == 0)
            {
                // 表头无 BOM 版本，通常是用料清单未按 BOM 展开（如仅主产品/手工新增），无需处理。
                Logger.Info(DesignatorHelper.LogTitle, "单据表头未取到 BOM 版本（FBOMID），跳过位号回填。");
                return;
            }

            // 2. 一次性取回位号字典（两个维度各查一次，避免逐行查库）
            Dictionary<long, string> byBomEntryId =
                DesignatorHelper.LoadDesignatorsByBomEntryId(this.Context, bomIds);
            Dictionary<string, string> byMaterial =
                DesignatorHelper.LoadDesignatorsByMaterial(this.Context, bomIds);

            if (byBomEntryId.Count == 0 && byMaterial.Count == 0)
            {
                Logger.Info(DesignatorHelper.LogTitle,
                    "BOM 子项未维护位号（BOM 内码：" + string.Join(",", bomIds.Distinct()) + "），跳过回填。");
                return;
            }

            // 3. 逐单、逐行回填
            int filled = 0;
            int skipped = 0;
            int missing = 0;
            string resolvedEntryKey = null;

            foreach (DynamicObject bill in billList)
            {
                long bomId = DesignatorHelper.GetBomId(bill);
                if (bomId <= 0L)
                {
                    continue;
                }

                string entryKey;
                DynamicObjectCollection entries =
                    DesignatorHelper.GetDesignatorEntryCollection(bill, out entryKey);
                if (entries == null)
                {
                    Logger.Info(DesignatorHelper.LogTitle,
                        "未定位到承载位号字段的子单据体，请检查用料清单是否已添加 " + DesignatorHelper.DesignatorField + " 字段。");
                    continue;
                }

                resolvedEntryKey = entryKey;

                foreach (DynamicObject entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string current = DesignatorHelper.GetString(entry, DesignatorHelper.DesignatorField);
                    if (!OverwriteExisting && !string.IsNullOrWhiteSpace(current))
                    {
                        skipped++;
                        continue;
                    }

                    string designator = ResolveDesignator(entry, bomId, byBomEntryId, byMaterial);
                    if (string.IsNullOrWhiteSpace(designator))
                    {
                        missing++;
                        continue;
                    }

                    if (DesignatorHelper.SetString(entry, DesignatorHelper.DesignatorField, designator))
                    {
                        filled++;
                    }
                }
            }

            string summary = string.Format(
                "位号回填完成：填充 {0} 行，保留原值 {1} 行，BOM 无位号 {2} 行（表体实体 {3}）。",
                filled, skipped, missing,
                resolvedEntryKey ?? "未识别");

            Logger.Info(DesignatorHelper.LogTitle, summary);

            if (ValidateAfterFill && ThrowOnFailure && missing > 0)
            {
                throw new KDBusinessException("BHD_PPBOM_DESIGNATOR",
                    string.Format("有 {0} 行子项在 BOM 中未维护位号，无法带出，请先完善 BOM 位号。", missing));
            }
        }

        /// <summary>
        /// 单个子项行的位号解析：先精确匹配 BOM 子项内码，再退回物料维度匹配。
        /// </summary>
        private static string ResolveDesignator(
            DynamicObject entry,
            long bomId,
            Dictionary<long, string> byBomEntryId,
            Dictionary<string, string> byMaterial)
        {
            // 优先：BOM 分录内码精确匹配
            long bomEntryId = DesignatorHelper.GetInt64(entry, "FBOMENTRYID");
            if (bomEntryId > 0L)
            {
                string hit;
                if (byBomEntryId.TryGetValue(bomEntryId, out hit) && !string.IsNullOrWhiteSpace(hit))
                {
                    return hit;
                }
            }

            // 兜底：BOM 内码 + 子项物料内码
            long materialId = DesignatorHelper.GetRefId(entry, "FMaterialId");
            if (materialId > 0L)
            {
                string hit;
                if (byMaterial.TryGetValue(DesignatorHelper.MaterialKey(bomId, materialId), out hit)
                    && !string.IsNullOrWhiteSpace(hit))
                {
                    return hit;
                }
            }

            return null;
        }
    }
}
