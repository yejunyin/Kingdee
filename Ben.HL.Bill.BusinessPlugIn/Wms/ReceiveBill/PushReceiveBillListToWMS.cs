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
    /// 收料通知单列表批量审核推送WMS + SRM回写插件
    /// 继承 AbstractListPlugIn，在列表批量审核完成后，
    /// 遍历审核成功的单据逐张执行推送逻辑（公共逻辑在 ReceiveBillPushService 中）
    /// </summary>
    [HotUpdate]
    [Description("收料通知单列表批量审核推送WMS-Ben")]
    public class PushReceiveBillListToWMS : AbstractListPlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        public override void AfterDoOperation(AfterDoOperationEventArgs e)
        {
            base.AfterDoOperation(e);

            if (e.Operation == null)
            {
                return;
            }
            string opName = e.Operation.Operation;
            if (!opName.Equals("Audit", StringComparison.OrdinalIgnoreCase)
                && !opName.Equals("PushWMS", StringComparison.OrdinalIgnoreCase))
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
                _benLog.Warn("收料通知单(列表)批量审核后未获取到选中行主键，跳过推送");
                return;
            }

            // 批量加载收料通知单 DynamicObject（FormId = PUR_ReceiveBill）
            List<DynamicObject> successBills = new List<DynamicObject>();
            try
            {
                FormMetadata meta = MetaDataServiceHelper.GetFormMetaData(this.Context, "PUR_ReceiveBill");
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
                _benLog.Error("收料通知单(列表)批量加载单据数据异常", loadEx);
            }

            if (successBills.Count == 0)
            {
                return;
            }

            int wmsOkCount = 0;
            int srmOkCount = 0;
            int skipCount = 0;
            int statusSavedCount = 0;
            var errors = new List<string>();

            IntegrationConfig config = IntegrationConfig.Load();

            foreach (DynamicObject billObj in successBills)
            {
                string billNo = ReceiveBillPushService.GetValue(billObj, "BillNo");
                try
                {
                    if (billObj == null) continue;

                    var svc = new ReceiveBillPushService(this.Context);

                    // 仅当分录中存在物料的拣货仓库(PickStockId.FNumber)为2010时，才推送WMS/SRM
                    if (!svc.ExistsPickStock2010(billObj))
                    {
                        // 非WMS仓物料，直接更新状态字段后跳过推送
                        try
                        {
                            long billId = ReceiveBillPushService.GetLong(billObj, "Id");
                            ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, "非WMS仓物料");
                        }
                        catch (Exception ex)
                        {
                            _benLog.Error(string.Format("收料通知单(列表)回写Fwmsstatus(非WMS仓)失败 BillNo={0}", billNo), ex);
                        }
                        skipCount++;
                        continue;
                    }

                    List<ReceiveBillPushService.ReceiveEntrySource> entries = svc.GetEntrySources(billObj);

                    // === WMS 推送 ===
                    string wmsMsg;
                    try
                    {
                        WmsSaveReceiveBillRequestModel wmsRequest = svc.BuildStandardApiRequest(billObj, entries);
                        string wmsJson = ReceiveBillPushService.Serialize(wmsRequest);
                        string wmsResponse = _apiClient.PostJson(config.WmsReceiveBillApiUrl, wmsJson);
                        _benLog.Info("收料通知单(列表)推送WMS请求报文：" + wmsJson + "；WMS返回：" + wmsResponse);
                        wmsMsg = ReceiveBillPushService.ParseWmsResponse(wmsResponse);
                        wmsOkCount++;
                    }
                    catch (Exception ex)
                    {
                        _benLog.Error(string.Format("收料通知单(列表)推送WMS异常 BillNo={0}", billNo), ex);
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
                            _benLog.Info("收料通知单(列表)SRM回写请求报文：" + srmJson + "；SRM返回：" + srmResponse);
                            srmMsg = ReceiveBillPushService.ParseSrmResponse(srmResponse);
                            srmOkCount++;
                        }
                        catch (Exception ex)
                        {
                            _benLog.Error(string.Format("收料通知单(列表)SRM回写异常 BillNo={0}", billNo), ex);
                            srmMsg = "回写异常：" + ex.Message;
                        }
                    }

                    // === 组装并回写 Fwmsstatus ===
                    string fwmsstatusVal = ReceiveBillPushService.BuildFwmsstatusValue(wmsMsg, srmMsg, srmSkipped);
                    try
                    {
                        long billId = ReceiveBillPushService.GetLong(billObj, "Id");
                        ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, fwmsstatusVal);
                        statusSavedCount++;
                    }
                    catch (Exception saveEx)
                    {
                        _benLog.Error(string.Format("收料通知单(列表)回写Fwmsstatus失败 BillNo={0}", billNo), saveEx);
                        errors.Add(string.Format("{0}: 回写状态失败 - {1}", billNo, saveEx.Message));
                    }
                }
                catch (Exception ex)
                {
                    _benLog.Error(string.Format("收料通知单(列表)推送异常 BillNo={0}", billNo), ex);
                    errors.Add(string.Format("{0}: {1}", billNo, ex.Message));
                }
            }

            string msg = string.Format(
                "列表批量审核推送完成：WMS成功{0}张，SRM回写成功{1}张，跳过(无2010仓库){2}张，状态回写{3}张",
                wmsOkCount, srmOkCount, skipCount, statusSavedCount);
            if (errors.Count > 0)
            {
                msg += string.Format("，异常{0}张：{1}", errors.Count, string.Join("; ", errors.Take(5)));
            }
            this.View.ShowMessage(msg);
        }
    }
}
