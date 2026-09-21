using System;
using System.Collections.Generic;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Orm.DataEntity;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 位号（Designator）回填公共逻辑。
    ///
    /// 业务背景：
    ///   丝印/贴片行业需要在 BOM 子项上标注元件位号（如 R1、C12、U3）。
    ///   生产订单按 BOM 展开生成生产用料清单时，需把 BOM 子项上的位号带到用料清单子项上，
    ///   供车间报工与贴片核对使用。
    ///
    /// 数据链路（已在本环境核实）：
    ///   T_PRD_MOENTRY / T_PRD_MO          —— 生产订单（PRD_MO），FBOMID = BOM 版本内码
    ///   T_ENG_BOM / T_ENG_BOMCHILD        —— BOM 单（ENG_BOM），子项明细含 F_BHD_Designator
    ///   T_PRD_PPBOM / T_PRD_PPBOMENTRY    —— 生产用料清单（PRD_PPBOM），表头 FBOMID = BOM 版本内码
    ///
    /// 匹配优先级：
    ///   1) 用料清单子项 FBOMENTRYID == BOM 子项 FENTRYID   —— 精确，规避同料号多行串位号
    ///   2) (用料清单表头 FBOMID, 子项 FMATERIALID) == (BOM FID, BOM 子项 FMATERIALID) —— 兜底
    /// </summary>
    internal static class DesignatorHelper
    {
        /// <summary>位号字段标识（BOM 子项 / 用料清单子项同名，二开前缀 BHD）。</summary>
        public const string DesignatorField = "F_BHD_Designator";

        /// <summary>BOM 子项（物料清单）物理表。</summary>
        public const string BomChildTable = "T_ENG_BOMCHILD";

        /// <summary>日志标题。</summary>
        public const string LogTitle = "BHD.PPBomDesignator";

        /// <summary>BOM 版本内码字段候选（不同版本/不同单据可能存在大小写差异）。</summary>
        private static readonly string[] BomIdFieldCandidates = { "FBomId", "FBOMID", "FBomID" };

        /// <summary>用料清单子项实体 key 候选（兜底用，优先自动探测）。</summary>
        private static readonly string[] EntryKeyCandidates =
        {
            "FPPBomEntry", "FPPBOMENTRY", "PPBomEntry", "FTreeEntity", "FEntity", "FEntry"
        };

        #region 字段读取

        /// <summary>
        /// 解析对象上的真实属性名（DynamicObject 索引大小写敏感，必须先解析真实名再取值）。
        /// 找不到返回 null。
        /// </summary>
        internal static string ResolveName(DynamicObject obj, string name)
        {
            if (obj == null || obj.DynamicObjectType == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            string matched = null;
            foreach (var p in obj.DynamicObjectType.Properties)
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    matched = p.Name;
                    break;
                }
            }

            return matched;
        }

        /// <summary>取基础资料字段的内码（ref 字段）。</summary>
        internal static long GetRefId(DynamicObject obj, string field)
        {
            string real = ResolveName(obj, field);
            if (real == null)
            {
                return 0L;
            }

            object value;
            try
            {
                value = obj[real];
            }
            catch (Exception)
            {
                return 0L;
            }

            if (value == null)
            {
                return 0L;
            }

            var refObj = value as DynamicObject;
            if (refObj != null)
            {
                string idName = ResolveName(refObj, "Id");
                if (idName == null)
                {
                    return 0L;
                }

                return ToLong(refObj[idName]);
            }

            return ToLong(value);
        }

        /// <summary>取整型/内码字段。</summary>
        internal static long GetInt64(DynamicObject obj, string field)
        {
            string real = ResolveName(obj, field);
            if (real == null)
            {
                return 0L;
            }

            try
            {
                return ToLong(obj[real]);
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        /// <summary>取文本字段。</summary>
        internal static string GetString(DynamicObject obj, string field)
        {
            string real = ResolveName(obj, field);
            if (real == null)
            {
                return null;
            }

            try
            {
                return Convert.ToString(obj[real]);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>写文本字段；字段不存在返回 false。</summary>
        internal static bool SetString(DynamicObject obj, string field, string value)
        {
            string real = ResolveName(obj, field);
            if (real == null)
            {
                return false;
            }

            try
            {
                obj[real] = value;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static long ToLong(object value)
        {
            if (value == null || value is DBNull)
            {
                return 0L;
            }

            if (value is long)
            {
                return (long)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is decimal)
            {
                return (long)(decimal)value;
            }

            long result;
            return long.TryParse(Convert.ToString(value), out result) ? result : 0L;
        }

        #endregion

        #region 单据结构解析

        /// <summary>
        /// 取用料清单表头的 BOM 版本内码。
        /// </summary>
        internal static long GetBomId(DynamicObject bill)
        {
            if (bill == null)
            {
                return 0L;
            }

            foreach (string field in BomIdFieldCandidates)
            {
                long id = GetRefId(bill, field);
                if (id > 0L)
                {
                    return id;
                }
            }

            return 0L;
        }

        /// <summary>
        /// 定位承载位号字段的子单据体集合。
        ///
        /// 策略：优先"自动探测"——遍历单据所有 DynamicObjectCollection 属性，
        /// 找出元素类型中包含位号字段的那一个。这样不依赖具体实体 key 命名，
        /// 换版本/改元数据也不会失效。探测不到再回退到候选 key 名单。
        /// </summary>
        internal static DynamicObjectCollection GetDesignatorEntryCollection(DynamicObject bill, out string entryKey)
        {
            entryKey = null;
            if (bill == null || bill.DynamicObjectType == null)
            {
                return null;
            }

            // 1) 自动探测
            foreach (var prop in bill.DynamicObjectType.Properties)
            {
                DynamicObjectCollection coll = AsCollection(bill, prop.Name);
                if (coll == null || coll.Count == 0)
                {
                    continue;
                }

                DynamicObject first = coll[0];
                if (first == null || first.DynamicObjectType == null)
                {
                    continue;
                }

                bool hasDesignator = false;
                foreach (var p in first.DynamicObjectType.Properties)
                {
                    if (string.Equals(p.Name, DesignatorField, StringComparison.OrdinalIgnoreCase))
                    {
                        hasDesignator = true;
                        break;
                    }
                }

                if (hasDesignator)
                {
                    entryKey = prop.Name;
                    return coll;
                }
            }

            // 2) 回退到候选 key
            foreach (string key in EntryKeyCandidates)
            {
                string real = ResolveName(bill, key);
                if (real == null)
                {
                    continue;
                }

                DynamicObjectCollection coll = AsCollection(bill, real);
                if (coll != null)
                {
                    entryKey = real;
                    return coll;
                }
            }

            return null;
        }

        private static DynamicObjectCollection AsCollection(DynamicObject obj, string name)
        {
            try
            {
                return obj[name] as DynamicObjectCollection;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region BOM 位号取数

        /// <summary>
        /// 按 BOM 子项内码取位号：Dictionary&lt;BOM子项FENTRYID, 位号&gt;。
        /// </summary>
        internal static Dictionary<long, string> LoadDesignatorsByBomEntryId(Context ctx, IEnumerable<long> bomIds)
        {
            var dict = new Dictionary<long, string>();
            var ids = (bomIds ?? Enumerable.Empty<long>()).Where(x => x > 0L).Distinct().ToList();
            if (ids.Count == 0)
            {
                return dict;
            }

            string sql = BuildSelect("FENTRYID", ids);
            var rows = DBUtils.ExecuteDynamicObject(ctx, sql);
            if (rows == null)
            {
                return dict;
            }

            foreach (DynamicObject row in rows)
            {
                long entryId = GetInt64(row, "FENTRYID");
                string designator = GetString(row, DesignatorField);
                if (entryId > 0L && !string.IsNullOrWhiteSpace(designator))
                {
                    dict[entryId] = designator.Trim();
                }
            }

            return dict;
        }

        /// <summary>
        /// 按 (BOM内码, 子项物料内码) 取位号：Dictionary&lt;"bomId|materialId", 位号&gt;。
        /// 用于 FBOMENTRYID 落空时的兜底匹配。
        /// </summary>
        internal static Dictionary<string, string> LoadDesignatorsByMaterial(Context ctx, IEnumerable<long> bomIds)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ids = (bomIds ?? Enumerable.Empty<long>()).Where(x => x > 0L).Distinct().ToList();
            if (ids.Count == 0)
            {
                return dict;
            }

            string sql = BuildSelect("FID, FMATERIALID", ids);
            var rows = DBUtils.ExecuteDynamicObject(ctx, sql);
            if (rows == null)
            {
                return dict;
            }

            foreach (DynamicObject row in rows)
            {
                long bomId = GetInt64(row, "FID");
                long materialId = GetInt64(row, "FMATERIALID");
                string designator = GetString(row, DesignatorField);

                if (bomId > 0L && materialId > 0L && !string.IsNullOrWhiteSpace(designator))
                {
                    dict[bomId + "|" + materialId] = designator.Trim();
                }
            }

            return dict;
        }

        /// <summary>
        /// 拼装 BOM 子项查询。
        /// 注：bomIds 已强制过滤为 long，直接拼接 IN 子句不存在注入风险。
        /// </summary>
        private static string BuildSelect(string extraColumns, List<long> bomIds)
        {
            return "SELECT " + extraColumns + ", " + DesignatorField
                 + " FROM " + BomChildTable
                 + " WHERE FID IN (" + string.Join(",", bomIds) + ")"
                 + " AND " + DesignatorField + " IS NOT NULL"
                 + " AND " + DesignatorField + " <> ''";
        }

        /// <summary>组合键。</summary>
        internal static string MaterialKey(long bomId, long materialId)
        {
            return bomId + "|" + materialId;
        }

        #endregion
    }
}
