using System;
using System.ComponentModel;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产汇报单(PRD_MORPT)审核后推送WMS，WMS完成入库后回写创建生产入库单(PRD_InStock)
    /// 推送数据格式为生产入库单标准API结构，WMS可直接调用PRD_InStock保存接口
    /// 字段名已按 PRD_MORPT 真实单据结构核对（表头+PRD_MORPTENTRY分录）
    /// </summary>
    [HotUpdate]
    [Description("生产汇报单审核推送WMS-Ben")]
    public class PushPrdReportFinToWMS : AbstractBillPlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);
            return;
            if (!e.Operation.Operation.Equals("Audit", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                DynamicObject billObj = this.Model.DataObject;
                if (billObj == null)
                {
                    this.View.ShowMessage("没有找到需要推送的生产汇报单数据");
                    return;
                }

                IntegrationConfig config = IntegrationConfig.Load();
                PrdInStockSaveRequestModel wmsRequest = PrdReportFinPushService.BuildPrdInStockDataFromReport(billObj);
                string jsonData = PrdReportFinPushService.Serialize(wmsRequest);
                _benLog.Info("生产汇报单推送WMS请求报文：" + jsonData);

                string response = _apiClient.PostJson(config.WmsPrdReportFinApiUrl, jsonData);
                this.View.ShowMessage("生产汇报单推送WMS成功。WMS返回：" + response);
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("生产汇报单推送WMS异常：" + ex.Message);
            }
        }
    }
}
