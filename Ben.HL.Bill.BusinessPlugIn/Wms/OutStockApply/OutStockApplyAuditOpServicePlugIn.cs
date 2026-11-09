using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 出库申请单(STK_OutStockApply)审核操作服务插件
    /// 继承 AbstractOperationServicePlugIn，挂在单据操作（Audit）上，
    /// 在操作事务提交后把单据组装成其他出库单(STK_MisDelivery)保存报文推送WMS，
    /// 供WMS回写其他出库单，自动覆盖单张/列表/API等所有审核入口。
    /// 推送逻辑复用 OutStockApplyPushService 公共服务。
    /// </summary>
    [HotUpdate]
    [Description("出库申请单审核推送WMS-操作服务插件-Ben")]
    public class OutStockApplyAuditOpServicePlugIn : AbstractOperationServicePlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        // STK_OutStockApply 完整 FormId，用于重新加载完整 DynamicObject
        private const string FormId = "STK_OutStockApply";

        /// <summary>
        /// 操作事务提交后触发，推送外部接口（WMS）。
        /// 用 BusinessDataServiceHelper.Load 重新加载完整单据对象，确保分录和基础资料字段齐全。
        /// </summary>
        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            if (e.DataEntitys == null || e.DataEntitys.Length == 0)
            {
                return;
            }

            // 1. 从操作结果里收集单据主键
            List<long> billIds = new List<long>();
            foreach (DynamicObject obj in e.DataEntitys)
            {
                long id;
                if (long.TryParse(ReceiveBillPushService.GetValue(obj, "Id"), out id) && id > 0)
                {
                    if (!billIds.Contains(id)) billIds.Add(id);
                }
            }

            if (billIds.Count == 0) return;

            // 2. 重新加载完整 DynamicObject（分录/基础资料子属性全齐）
            List<DynamicObject> fullBills = new List<DynamicObject>();
            try
            {
                FormMetadata meta = MetaDataServiceHelper.GetFormMetaData(this.Context, FormId);
                if (meta != null && meta.BusinessInfo != null)
                {
                    object[] pkArr = billIds.Cast<object>().ToArray();
                    var objs = BusinessDataServiceHelper.Load(this.Context, pkArr, meta.BusinessInfo.GetDynamicObjectType());
                    if (objs != null)
                    {
                        foreach (var obj in objs)
                        {
                            var dyn = obj as DynamicObject;
                            if (dyn != null) fullBills.Add(dyn);
                        }
                    }
                }
            }
            catch (Exception loadEx)
            {
                _benLog.Error("出库申请单(操作服务)加载完整单据异常", loadEx);
                return;
            }

            // 3. 逐张组装其他出库单报文并推送 WMS，收集结果
            IntegrationConfig config = IntegrationConfig.Load();
            List<string> pushResults = new List<string>();
            int okCount = 0;
            int failCount = 0;
            int skipCount = 0;

            foreach (DynamicObject billObj in fullBills)
            {
                string billNo = ReceiveBillPushService.GetValue(billObj, "BillNo");
                try
                {
                    var svc = new OutStockApplyPushService(this.Context);
                    MisDeliverySaveRequestModel wmsRequest = svc.BuildMisDeliveryRequest(billObj);

                    // 无有效物料分录：跳过推送
                    if (wmsRequest.data.Model.FEntity.Count == 0)
                    {
                        skipCount++;
                        pushResults.Add(string.Format("○ {0} 无有效分录，跳过推送", billNo));
                        continue;
                    }

                    string jsonData = ReceiveBillPushService.Serialize(wmsRequest);
                    _benLog.Info(string.Format("出库申请单(操作服务)推送WMS请求报文 BillNo={0}：{1}", billNo, jsonData));

                    string response = _apiClient.PostJson(config.WmsOutStockApplyApiUrl, jsonData);
                    _benLog.Info(string.Format("出库申请单(操作服务)推送WMS返回 BillNo={0}：{1}", billNo, response));

                    pushResults.Add(string.Format("✓ {0} 推送WMS成功", billNo));
                    okCount++;
                }
                catch (Exception ex)
                {
                    _benLog.Error(string.Format("出库申请单(操作服务)推送WMS异常 BillNo={0}", billNo), ex);
                    pushResults.Add(string.Format("✗ {0} 推送WMS失败：{1}", billNo, ex.Message));
                    failCount++;
                }
            }

            // 4. 汇总弹窗
            OpServiceResultHelper.ShowOperationResult(
                this.OperationResult,
                this.Context,
                "出库申请单推送WMS结果",
                string.Format("成功 {0} 张，失败 {1} 张，跳过 {2} 张，共 {3} 张",
                    okCount, failCount, skipCount, okCount + failCount + skipCount),
                pushResults);
        }
    }
}
