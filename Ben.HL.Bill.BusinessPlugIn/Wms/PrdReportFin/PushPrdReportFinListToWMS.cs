using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Core.List;
using Kingdee.BOS.Core.List.PlugIn;
using Kingdee.BOS.Core.Metadata;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.ServiceHelper;
using Kingdee.BOS.Util;
using log4net;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 生产汇报单(PRD_MORPT)列表批量审核推送WMS插件
    /// 继承 AbstractListPlugIn，在列表批量审核完成后，
    /// 遍历审核成功的单据逐张执行推送逻辑（组装生产入库单 PRD_InStock 标准API结构推送到WMS）
    /// </summary>
    [HotUpdate]
    [Description("生产汇报单列表批量审核推送WMS-Ben")]
    public class PushPrdReportFinListToWMS : AbstractListPlugIn
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

            // 列表插件：从用户选中的行取主键，再批量加载完整 DynamicObject
            List<long> billIds = new List<long>();
            ListSelectedRowCollection selectedRows = this.ListView.SelectedRowsInfo;

            if (selectedRows != null && selectedRows.Count > 0)
            {
                foreach (ListSelectedRow row in selectedRows)
                {
                    if (row != null && row.PrimaryKeyValue != null)
                    {
                        long id;
                        if (long.TryParse(row.PrimaryKeyValue.ToString(), out id) && id > 0)
                        {
                            billIds.Add(id);
                        }
                    }
                }
            }

            if (billIds.Count == 0)
            {
                _benLog.Warn("生产汇报单(列表)批量审核后未获取到选中行主键，跳过推送");
                return;
            }

            // 批量加载生产汇报单 DynamicObject（FormId = PRD_MORPT）
            List<DynamicObject> successBills = new List<DynamicObject>();
            try
            {
                FormMetadata meta = MetaDataServiceHelper.GetFormMetaData(this.Context, "PRD_MORPT");
                if (meta != null && meta.BusinessInfo != null)
                {
                    object[] pkArr = billIds.Distinct().Cast<object>().ToArray();
                    var objs = BusinessDataServiceHelper.Load(this.Context, pkArr, meta.BusinessInfo.GetDynamicObjectType());
                    if (objs != null)
                    {
                        foreach (var obj in objs)
                        {
                            var dyn = obj as DynamicObject;
                            if (dyn != null) successBills.Add(dyn);
                        }
                    }
                }
            }
            catch (Exception loadEx)
            {
                _benLog.Error("生产汇报单(列表)批量加载单据数据异常", loadEx);
            }

            if (successBills.Count == 0)
            {
                return;
            }

            int okCount = 0;
            var errors = new List<string>();
            IntegrationConfig config = IntegrationConfig.Load();

            foreach (DynamicObject billObj in successBills)
            {
                try
                {
                    if (billObj == null) continue;

                    PrdInStockSaveRequestModel wmsRequest = PrdReportFinPushService.BuildPrdInStockDataFromReport(billObj);
                    string jsonData = PrdReportFinPushService.Serialize(wmsRequest);
                    _benLog.Info("生产汇报单(列表)推送WMS请求报文：" + jsonData);

                    string response = _apiClient.PostJson(config.WmsPrdReportFinApiUrl, jsonData);
                    _benLog.Info("生产汇报单(列表)推送WMS返回：" + response);
                    okCount++;
                }
                catch (Exception ex)
                {
                    string billNo = PrdReportFinPushService.GetValue(billObj, "BillNo");
                    _benLog.Error(string.Format("生产汇报单(列表)推送异常 BillNo={0}", billNo), ex);
                    errors.Add(string.Format("{0}: {1}", billNo, ex.Message));
                }
            }

            string msg = string.Format("列表批量审核推送完成：WMS成功{0}张", okCount);
            if (errors.Count > 0)
            {
                msg += string.Format("，失败{0}张：{1}", errors.Count, string.Join("; ", errors.Take(5)));
            }
            this.View.ShowMessage(msg);
        }
    }
}
