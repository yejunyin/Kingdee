using System;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn.Payment
{
    /// <summary>
    /// 付款申请单(PAY_PayApply)提交操作服务插件
    /// 继承 AbstractOperationServicePlugIn，挂在单据操作（Submit）上，
    /// 当单据类型为"采购业务付款单"时，自动给单据头 F_Description 赋值：
    ///   支付（预付）+ 往来单位 + 第一条明细物料名称 + 款
    /// </summary>
    [HotUpdate]
    [Description("付款申请单提交自动生成摘要-操作服务插件-Ben")]
    public class PayApplySubmitOpServicePlugIn : AbstractOperationServicePlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.Payment");

        /// <summary>
        /// 需要预加载的字段 Key 列表
        /// </summary>
        public override void OnPreparePropertys(PreparePropertysEventArgs e)
        {
            base.OnPreparePropertys(e);

            // 单据头字段
            e.FieldKeys.Add("FBillTypeID");      // 单据类型
            e.FieldKeys.Add("FCONTACTUNIT");     // 往来单位
            e.FieldKeys.Add("F_Description");    // 单据头备注/摘要

            // 单据体字段
            e.FieldKeys.Add("FPAYAPPLYENTRY");   // 单据体分录集合
            e.FieldKeys.Add("FMATERIALID");      // 物料（分录基础资料字段，取其 Name）
        }

        /// <summary>
        /// 执行操作事务前，修改单据数据
        /// 适用于 Submit（提交）、Audit（审核）等所有操作，通过元数据绑定后仅在提交时触发
        /// </summary>
        public override void BeginOperationTransaction(BeginOperationTransactionArgs e)
        {
            base.BeginOperationTransaction(e);

            if (e.DataEntitys == null || !e.DataEntitys.Any()) return;

            foreach (DynamicObject billObj in e.DataEntitys)
            {
                try
                {
                    // 1. 判断单据类型是否为"采购付款申请"
                    string billTypeName = GetBaseDataName(billObj, "FBillTypeID");
                    if (string.IsNullOrWhiteSpace(billTypeName) || billTypeName != "采购付款申请")
                    {
                        continue;
                    }

                    // 2. 判断 F_Description 是否为空，不为空则跳过
                    string existingDescription = GetValue(billObj, "F_Description");
                    if (!string.IsNullOrWhiteSpace(existingDescription))
                    {
                        _benLog.Info(string.Format(
                            "付款申请单 F_Description 已有值，跳过自动生成：BillNo={0}, Description={1}",
                            GetValue(billObj, "BillNo"),
                            existingDescription));
                        continue;
                    }

                    // 3. 获取往来单位名称
                    string contactUnitName = GetBaseDataName(billObj, "FCONTACTUNIT");

                    // 4. 获取第一条明细的物料名称
                    string materialNames = GetFirstMaterialName(billObj);

                    // 5. 拼接摘要
                    string description = string.Format("支付（预付）{0}{1}款",
                        contactUnitName ?? "",
                        materialNames ?? "");

                    // 6. 赋值给单据头 F_Description
                    billObj["F_Description"] = description;

                    _benLog.Info(string.Format(
                        "付款申请单提交生成摘要：BillNo={0}, Description={1}",
                        GetValue(billObj, "BillNo"),
                        description));
                }
                catch (Exception ex)
                {
                    _benLog.Error(string.Format(
                        "付款申请单(提交操作服务)处理异常 BillNo={0}",
                        GetValue(billObj, "BillNo")), ex);
                }
            }
        }

        /// <summary>
        /// 获取单据体第一条明细的物料名称
        /// </summary>
        private string GetFirstMaterialName(DynamicObject billObj)
        {
            // 付款申请单分录标识为 FPAYAPPLYENTRY
            DynamicObjectCollection entryRows = null;

            if (billObj.DynamicObjectType.Properties.Contains("FPAYAPPLYENTRY"))
            {
                entryRows = billObj["FPAYAPPLYENTRY"] as DynamicObjectCollection;
            }

            if (entryRows == null || entryRows.Count == 0) return "";

            string matName = GetBaseDataName(entryRows[0], "FMATERIALID");
            return string.IsNullOrWhiteSpace(matName) ? "" : "" + matName.Trim();
        }

        /// <summary>
        /// 获取基础资料字段的名称 (Name)
        /// </summary>
        private string GetBaseDataName(DynamicObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key)) return "";
            if (!obj.DynamicObjectType.Properties.Contains(key)) return "";

            object val = obj[key];
            DynamicObject dyObj = val as DynamicObject;
            if (dyObj != null)
            {
                if (dyObj.DynamicObjectType.Properties.Contains("Name"))
                {
                    return dyObj["Name"] == null ? "" : dyObj["Name"].ToString().Trim();
                }
                if (dyObj.DynamicObjectType.Properties.Contains("FMATERIALNAME"))
                {
                    return dyObj["FMATERIALNAME"] == null ? "" : dyObj["FMATERIALNAME"].ToString().Trim();
                }
            }
            return val == null ? "" : val.ToString().Trim();
        }

        /// <summary>
        /// 通用取值辅助
        /// </summary>
        private string GetValue(DynamicObject obj, string key)
        {
            if (obj == null || string.IsNullOrWhiteSpace(key)) return "";
            if (!obj.DynamicObjectType.Properties.Contains(key)) return "";
            object val = obj[key];
            return val == null || val == DBNull.Value ? "" : val.ToString().Trim();
        }
    }
}
