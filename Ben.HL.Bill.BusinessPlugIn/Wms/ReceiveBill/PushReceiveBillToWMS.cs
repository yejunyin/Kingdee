using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("收料通知单审核推送WMS-Ben")]
    public class PushReceiveBillToWMS : AbstractBillPlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (e.Operation == null || !e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DynamicObject billObj = this.Model.DataObject;
            if (billObj == null)
            {
                this.View.ShowMessage("没有找到需要推送的单据数据");
                return;
            }

            var svc = new ReceiveBillPushService(this.Context);

            // 仅当分录中存在物料的拣货仓库(PickStockId.FNumber)为2010时，才推送WMS/SRM
            if (!svc.ExistsPickStock2010(billObj))
            {
                // 非WMS仓物料，直接更新状态字段后返回
                try
                {
                    long billId = ReceiveBillPushService.GetLong(billObj, "Id");
                    ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, "非WMS仓物料");
                }
                catch (Exception ex)
                {
                    _benLog.Error("收料通知单回写Fwmsstatus(非WMS仓)失败", ex);
                }
                return;
            }

            IntegrationConfig config = IntegrationConfig.Load();
            List<ReceiveBillPushService.ReceiveEntrySource> entries = svc.GetEntrySources(billObj);

            // === WMS 推送 ===
            string wmsMsg;
            try
            {
                WmsSaveReceiveBillRequestModel wmsRequest = svc.BuildStandardApiRequest(billObj, entries);
                string wmsJson = ReceiveBillPushService.Serialize(wmsRequest);
                string wmsResponse = _apiClient.PostJson(config.WmsReceiveBillApiUrl, wmsJson);
                _benLog.Info("收料通知单推送WMS请求报文：" + wmsJson + "；WMS返回：" + wmsResponse);
                wmsMsg = ReceiveBillPushService.ParseWmsResponse(wmsResponse);
            }
            catch (Exception ex)
            {
                _benLog.Error("收料通知单推送WMS异常", ex);
                wmsMsg = "推送异常：" + ex.Message;
            }

            // === SRM 推送 ===
            string srmMsg = null;
            bool srmSkipped = false;
            string srmSourceNo = ReceiveBillPushService.GetValue(billObj, "HL_SRM_Source_NO");
            if (string.IsNullOrWhiteSpace(srmSourceNo))
            {
                srmSkipped = true;
            }
            else
            {
                try
                {
                    SrmReceiveRequestModel srmRequest = svc.BuildSrmRequest(billObj, entries, config);
                    string srmJson = ReceiveBillPushService.Serialize(srmRequest);
                    string srmResponse = _apiClient.PostSoap(config.SrmApiUrl, srmJson);
                    _benLog.Info("收料通知单SRM回写请求报文：" + srmJson + "；SRM返回：" + srmResponse);
                    srmMsg = ReceiveBillPushService.ParseSrmResponse(srmResponse);
                }
                catch (Exception ex)
                {
                    _benLog.Error("收料通知单SRM回写异常", ex);
                    srmMsg = "回写异常：" + ex.Message;
                }
            }

            // === 组装并回写 Fwmsstatus ===
            string fwmsstatusVal = ReceiveBillPushService.BuildFwmsstatusValue(wmsMsg, srmMsg, srmSkipped);
            try
            {
                long billId = ReceiveBillPushService.GetLong(billObj, "Id");
                ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, fwmsstatusVal);
            }
            catch (Exception saveEx)
            {
                _benLog.Error("收料通知单回写Fwmsstatus失败", saveEx);
            }

            this.View.ShowMessage(fwmsstatusVal);
        }
    }
}
