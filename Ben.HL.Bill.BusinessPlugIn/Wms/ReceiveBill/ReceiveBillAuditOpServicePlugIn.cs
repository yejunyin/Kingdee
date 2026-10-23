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
    /// 收料通知单(PUR_ReceiveBill)审核操作服务插件
    /// 继承 AbstractOperationServicePlugIn，挂在单据操作（Audit）上，
    /// 在操作事务提交后推送WMS + SRM并回写FWMSSTATUS，自动覆盖单张/列表/API等所有审核入口。
    /// 推送逻辑复用 ReceiveBillPushService 公共服务。
    /// </summary>
    [HotUpdate]
    [Description("收料通知单审核推送WMS-操作服务插件-Ben")]
    public class ReceiveBillAuditOpServicePlugIn : AbstractOperationServicePlugIn
    {
        private static readonly ILog _benLog = LogManager.GetLogger("Ben.WMS");
        private readonly WmsApiClient _apiClient = new WmsApiClient();

        private const string FormId = "PUR_ReceiveBill";

        public override void AfterExecuteOperationTransaction(AfterExecuteOperationTransaction e)
        {
            base.AfterExecuteOperationTransaction(e);

            if (e.DataEntitys == null || e.DataEntitys.Length == 0)
            {
                return;
            }

            // 1. 收集单据主键
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
                _benLog.Error("收料通知单(操作服务)加载完整单据异常", loadEx);
                return;
            }

            // 3. 逐张处理：判断WMS仓 → 推送WMS → 推送SRM → 回写FWMSSTATUS
            IntegrationConfig config = IntegrationConfig.Load();
            int wmsOkCount = 0;
            int srmOkCount = 0;
            int skipCount = 0;
            int statusSavedCount = 0;

            foreach (DynamicObject billObj in fullBills)
            {
                string billNo = ReceiveBillPushService.GetValue(billObj, "BillNo");
                long billId = ReceiveBillPushService.GetLong(billObj, "Id");
                try
                {
                    var svc = new ReceiveBillPushService(this.Context);

                    // === 非WMS仓物料：直接回写状态跳过推送 ===
                    if (!svc.ExistsPickStock2010(billObj))
                    {
                        try
                        {
                            ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, "非WMS仓物料");
                            statusSavedCount++;
                        }
                        catch (Exception ex)
                        {
                            _benLog.Error(string.Format("收料通知单(操作服务)回写Fwmsstatus(非WMS仓)失败 BillNo={0}", billNo), ex);
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
                        _benLog.Info(string.Format("收料通知单(操作服务)推送WMS BillNo={0} 请求：{1}；返回：{2}", billNo, wmsJson, wmsResponse));
                        wmsMsg = ReceiveBillPushService.ParseWmsResponse(wmsResponse);
                        wmsOkCount++;
                    }
                    catch (Exception ex)
                    {
                        _benLog.Error(string.Format("收料通知单(操作服务)推送WMS异常 BillNo={0}", billNo), ex);
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
                            _benLog.Info(string.Format("收料通知单(操作服务)SRM回写 BillNo={0} 请求：{1}；返回：{2}", billNo, srmJson, srmResponse));
                            srmMsg = ReceiveBillPushService.ParseSrmResponse(srmResponse);
                            srmOkCount++;
                        }
                        catch (Exception ex)
                        {
                            _benLog.Error(string.Format("收料通知单(操作服务)SRM回写异常 BillNo={0}", billNo), ex);
                            srmMsg = "回写异常：" + ex.Message;
                        }
                    }

                    // === 组装并回写 Fwmsstatus ===
                    string fwmsstatusVal = ReceiveBillPushService.BuildFwmsstatusValue(wmsMsg, srmMsg, srmSkipped);
                    try
                    {
                        ReceiveBillPushService.SaveFwmsstatus(this.Context, billId, fwmsstatusVal);
                        statusSavedCount++;
                    }
                    catch (Exception saveEx)
                    {
                        _benLog.Error(string.Format("收料通知单(操作服务)回写Fwmsstatus失败 BillNo={0}", billNo), saveEx);
                    }
                }
                catch (Exception ex)
                {
                    _benLog.Error(string.Format("收料通知单(操作服务)推送异常 BillNo={0}", billNo), ex);
                }
            }

            _benLog.Info(string.Format(
                "收料通知单(操作服务)审核推送完成：WMS成功{0}，SRM成功{1}，跳过(非WMS仓){2}，状态回写{3}",
                wmsOkCount, srmOkCount, skipCount, statusSavedCount));
        }
    }
}
