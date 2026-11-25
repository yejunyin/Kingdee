using System;
using Kingdee.BOS.Orm.DataEntity;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// DynamicObject 取值辅助工具类（供所有 WMS 推送插件及其他插件复用）。
    /// 候选字段名依次尝试取值，安全兼容金蝶元数据不同命名口径。
    /// </summary>
    public static class DynamicObjectHelper
    {
        /// <summary>
        /// 按候选字段名依次尝试取基础资料 DynamicObject，返回第一个非空的。
        /// 字段不存在或取值异常时自动跳过继续下一个候选。
        /// </summary>
        /// <param name="source">DynamicObject 数据源（单据头/分录行）</param>
        /// <param name="fieldNames">候选字段名数组，例如 "BillTypeId", "FBillTypeId"</param>
        /// <returns>第一个取到的 DynamicObject；全部候选都取不到返回 null</returns>
        public static DynamicObject TryGetBaseData(DynamicObject source, params string[] fieldNames)
        {
            if (source == null || fieldNames == null) return null;
            foreach (string fieldName in fieldNames)
            {
                try
                {
                    object val = source[fieldName];
                    var dyn = val as DynamicObject;
                    if (dyn != null) return dyn;
                }
                catch
                {
                    // 字段不存在或取值异常，继续下一个候选
                }
            }
            return null;
        }

        /// <summary>
        /// 按候选字段名依次尝试取 DynamicObjectCollection（单据体/关联表），返回第一个 Count > 0 的。
        /// </summary>
        public static DynamicObjectCollection TryGetCollection(DynamicObject source, params string[] fieldNames)
        {
            if (source == null || fieldNames == null) return null;
            foreach (string fieldName in fieldNames)
            {
                try
                {
                    object val = source[fieldName];
                    var col = val as DynamicObjectCollection;
                    if (col != null && col.Count > 0) return col;
                }
                catch
                {
                    // 字段不存在或取值异常，继续下一个候选
                }
            }
            return null;
        }

        /// <summary>
        /// 按候选字段名依次取基础资料编码（FNumber），返回第一个有效的编码值。
        /// 底层调用 TryGetBaseData + GetValue(baseData, "Number")。
        /// </summary>
        public static string GetBaseNumber(DynamicObject source, params string[] fieldNames)
        {
            if (source == null || fieldNames == null) return string.Empty;
            foreach (string fieldName in fieldNames)
            {
                DynamicObject baseData = TryGetBaseData(source, fieldName);
                if (baseData != null)
                {
                    string number = GetValue(baseData, "Number");
                    if (!string.IsNullOrWhiteSpace(number))
                    {
                        return number;
                    }
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 安全提取 DynamicObject 指定字段的字符串值。
        /// 字段不存在 / 为 null / 取值异常均返回 string.Empty。
        /// </summary>
        public static string GetValue(DynamicObject source, string fieldName)
        {
            if (source == null || string.IsNullOrWhiteSpace(fieldName)) return string.Empty;
            try
            {
                object val = source[fieldName];
                return val == null ? string.Empty : val.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 安全提取 long 值
        /// </summary>
        public static long GetLong(DynamicObject source, string fieldName)
        {
            long value;
            return long.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }

        /// <summary>
        /// 安全提取 decimal 值
        /// </summary>
        public static decimal GetDecimal(DynamicObject source, string fieldName)
        {
            decimal value;
            return decimal.TryParse(GetValue(source, fieldName), out value) ? value : 0;
        }
    }
}
