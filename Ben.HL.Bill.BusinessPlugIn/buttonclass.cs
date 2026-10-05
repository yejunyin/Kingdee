using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Util;
using Kingdee.BOS.Core.Bill.PlugIn.Args;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [HotUpdate]
    [Description("生产订单表单插件-MES接口校验与保存后用料清单位号回填")]
    public class buttonclass : AbstractBillPlugIn
    {
        // 标志位：是否需要阻止操作
        private bool _needBlockOperation = false;
        // 错误消息
        private string _errorMessage = string.Empty;

        public override void OnInitialize(InitializeEventArgs e)
        {
            base.OnInitialize(e);
        }

        /// <summary>
        /// 保存后事件：触发生产用料清单/单据位号回填逻辑
        /// </summary>
        public override void AfterSave(AfterSaveEventArgs e)
        {
            base.AfterSave(e);

            // 仅在保存成功时执行
                try
                {
                    FillPPBomDesignators();
                }
                catch (Exception ex)
                {
                    // 位号回填属于附加逻辑，若报错建议提示但不一定要阻断保存流程
                    this.View.ShowMessage($"保存成功，但更新位号时发生异常：{ex.Message}", MessageBoxType.Error);
                }
        }

        #region 位号回填核心逻辑 (原服务插件合并)

        /// <summary>
        /// 保存后解析并回填位号
        /// </summary>
        private void FillPPBomDesignators()
        {
            DynamicObject dataObj = this.Model.DataObject;
            if (dataObj == null) return;

            // 获取当前单据的 FormId
            string formId = this.View.BillBusinessInfo.GetForm().Id;

            // 1. 如果当前界面是生产订单（PRD_MO）
            if (formId.Equals("PRD_MO", StringComparison.OrdinalIgnoreCase))
            {
                long moId = Convert.ToInt64(dataObj["Id"]);
                if (moId > 0)
                {
                    // 生产订单保存后，后台用料清单已生成到数据库，直接执行 SQL 更新数据库中的用料清单位号
                    ExecuteUpdatePPBomDesignatorByMoId(moId);
                }
                return;
            }

            // 2. 如果当前界面是生产用料清单（PRD_PPBOM）
            if (formId.Equals("PRD_PPBOM", StringComparison.OrdinalIgnoreCase))
            {
                // 尝试获取用料清单表体（标准常用 EntityKey 为 PPBOMENTRY 或 FEntity）
                string entityKey = this.Model.BusinessInfo.GetEntity("PPBOMENTRY") != null ? "PPBOMENTRY" : "FEntity";
                DynamicObjectCollection entryRows = dataObj[entityKey] as DynamicObjectCollection;

                if (entryRows != null && entryRows.Count > 0)
                {
                    bool isUpdated = false;
                    foreach (DynamicObject row in entryRows)
                    {
                        long materialId = GetValueLong(row, "MaterialID_Id", "MaterialID");
                        if (materialId == 0) continue;

                        long bomId = GetValueLong(row, "BOMID_Id", "BOMID");
                        long moEntryId = GetValueLong(row, "MoEntryId");

                        if (bomId == 0 && moEntryId > 0)
                        {
                            bomId = GetBomIdFromMoEntry(moEntryId);
                        }
                        if (bomId == 0)
                        {
                            bomId = GetDefaultBomId(materialId);
                        }

                        if (bomId > 0)
                        {
                            HashSet<string> designatorSet = new HashSet<string>();
                            HashSet<long> visitedBomIds = new HashSet<long>();

                            GetDesignatorsFromBomChild(bomId, materialId, designatorSet, visitedBomIds);

                            if (designatorSet.Count > 0)
                            {
                                string designators = string.Join(",", designatorSet);
                                row["F_BHD_Designator"] = designators;
                                isUpdated = true;
                            }
                        }
                    }

                    if (isUpdated)
                    {
                        this.View.UpdateView("F_BHD_Designator"); // 刷新用料清单界面视图
                    }
                }
            }
        }

        /// <summary>
        /// 生产订单保存后，通过 MoId 追溯关联的用料清单并直接更新数据库中的位号字段
        /// </summary>
        private void ExecuteUpdatePPBomDesignatorByMoId(long moId)
        {
            string querySql = $@"
                SELECT T2.FENTRYID, T2.FMATERIALID, T2.FBOMID, T2.FMOENTRYID
                FROM T_PRD_PPBOM T1
                INNER JOIN T_PRD_PPBOMENTRY T2 ON T1.FID = T2.FID
                WHERE T1.FMOID = {moId}";

            DynamicObjectCollection rows = DBUtils.ExecuteDynamicObject(this.Context, querySql);
            if (rows == null || rows.Count == 0) return;

            foreach (DynamicObject row in rows)
            {
                long entryId = Convert.ToInt64(row["FENTRYID"]);
                long materialId = Convert.ToInt64(row["FMATERIALID"]);
                long bomId = Convert.ToInt64(row["FBOMID"]);
                long moEntryId = Convert.ToInt64(row["FMOENTRYID"]);

                if (bomId == 0 && moEntryId > 0)
                {
                    bomId = GetBomIdFromMoEntry(moEntryId);
                }
                if (bomId == 0)
                {
                    bomId = GetDefaultBomId(materialId);
                }

                if (bomId > 0)
                {
                    HashSet<string> designatorSet = new HashSet<string>();
                    HashSet<long> visitedBomIds = new HashSet<long>();

                    GetDesignatorsFromBomChild(bomId, materialId, designatorSet, visitedBomIds);

                    if (designatorSet.Count > 0)
                    {
                        string designators = string.Join(",", designatorSet);
                        string updateSql = $"UPDATE T_PRD_PPBOMENTRY SET F_BHD_DESIGNATOR = '{designators}' WHERE FENTRYID = {entryId}";
                        DBUtils.Execute(this.Context, updateSql);
                    }
                }
            }
        }

        private long GetBomIdFromMoEntry(long moEntryId)
        {
            string sql = $"SELECT FBOMID FROM T_PRD_MOENTRY WHERE FENTRYID = {moEntryId}";
            return DBUtils.ExecuteScalar<long>(this.Context, sql, 0, null);
        }

        private void GetDesignatorsFromBomChild(long bomId, long targetMaterialId, HashSet<string> designatorSet, HashSet<long> visitedBomIds)
        {
            if (bomId <= 0 || visitedBomIds.Contains(bomId)) return;
            visitedBomIds.Add(bomId);

            string sql = $@"
                SELECT 
                    C.FMATERIALID AS CHILD_MATERIALID,
                    C.FBOMID AS CHILD_BOMID,
                    C.F_BHD_DESIGNATOR
                FROM T_ENG_BOM B
                INNER JOIN T_ENG_BOMCHILD C ON B.FID = C.FID
                WHERE B.FID = {bomId}";

            DynamicObjectCollection rows = DBUtils.ExecuteDynamicObject(this.Context, sql);
            if (rows == null || rows.Count == 0) return;

            List<long> nextLevelBomIds = new List<long>();

            foreach (DynamicObject row in rows)
            {
                long childMaterialId = Convert.ToInt64(row["CHILD_MATERIALID"]);
                string designator = row["F_BHD_DESIGNATOR"] != null ? row["F_BHD_DESIGNATOR"].ToString().Trim() : "";

                if (childMaterialId == targetMaterialId && !string.IsNullOrEmpty(designator))
                {
                    string[] parts = designator.Split(new char[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        designatorSet.Add(part.Trim());
                    }
                }

                long childBomId = Convert.ToInt64(row["CHILD_BOMID"]);
                if (childBomId == 0 && childMaterialId > 0)
                {
                    childBomId = GetDefaultBomId(childMaterialId);
                }

                if (childBomId > 0)
                {
                    nextLevelBomIds.Add(childBomId);
                }
            }

            foreach (long childBomId in nextLevelBomIds)
            {
                GetDesignatorsFromBomChild(childBomId, targetMaterialId, designatorSet, visitedBomIds);
            }
        }

        private long GetDefaultBomId(long materialId)
        {
            string sql = $@"
                SELECT TOP 1 FID 
                FROM T_ENG_BOM 
                WHERE FMATERIALID = {materialId} 
                  AND FDOCUMENTSTATUS = 'C' 
                  AND FFORBIDSTATUS = 'A'
                ORDER BY FISDEFAULT DESC, FID DESC";

            return DBUtils.ExecuteScalar<long>(this.Context, sql, 0, null);
        }

        private long GetValueLong(DynamicObject row, params string[] propertyNames)
        {
            if (row == null) return 0;
            foreach (var prop in propertyNames)
            {
                if (row.DynamicObjectType.Properties.Contains(prop))
                {
                    object val = row[prop];
                    DynamicObject dyObj = val as DynamicObject;
                    if (dyObj != null)
                    {
                        if (dyObj.DynamicObjectType.Properties.Contains("Id"))
                        {
                            long idVal = Convert.ToInt64(dyObj["Id"]);
                            if (idVal > 0) return idVal;
                        }
                    }
                    else if (val != null && val != DBNull.Value)
                    {
                        long result = 0;
                        if (long.TryParse(val.ToString(), out result) && result > 0)
                        {
                            return result;
                        }
                    }
                }
            }
            return 0;
        }

        #endregion

        #region MES校验与按钮点击逻辑 (原有表单插件)

        public override void BeforeDoOperation(BeforeDoOperationEventArgs e)
        {
            base.BeforeDoOperation(e);

            if (_needBlockOperation)
            {
                e.Cancel = true;
                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                _needBlockOperation = false;
                return;
            }
        }

        public override void EntryBarItemClick(BarItemClickEventArgs e)
        {
            base.EntryBarItemClick(e);

            _needBlockOperation = false;
            _errorMessage = string.Empty;

            switch (e.BarItemKey)
            {
                case "tbBtnToRelease":
                case "tbBtnToStart":
                    ExecuteToRelease();
                    break;
                case "tbBtnUndoToPlanConfirm":
                case "tbBtnUndoToPlan":
                    ExecuteUndoToPlanConfirm();
                    break;
                default:
                    break;
            }
        }

        private void ExecuteToRelease()
        {
            try
            {
                DynamicObject dataPacket = this.Model.DataObject;
                DynamicObjectCollection entrys = dataPacket["TreeEntity"] as DynamicObjectCollection;
                string productionOrderCode = dataPacket["BillNo"]?.ToString();

                if (string.IsNullOrEmpty(productionOrderCode))
                {
                    _needBlockOperation = true;
                    _errorMessage = "无法获取生产订单号，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                if (entrys == null || entrys.Count == 0)
                {
                    _needBlockOperation = true;
                    _errorMessage = "无法获取分录数据，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                DynamicObject firstEntry = entrys[0];
                int scheduledProducedUnits = Convert.ToInt32(firstEntry["Qty"]);
                if (scheduledProducedUnits <= 0)
                {
                    _needBlockOperation = true;
                    _errorMessage = "计划生产数量必须大于0，请检查单据";
                    this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
                    return;
                }

                string orderERPStatus = "下达";
                string url = "http://192.168.1.6:80/iMark/v1/DBProductionOrderInfo/modifyIfNoProcessOrder";

                var bodyData = new
                {
                    productionOrderCode = productionOrderCode,
                    scheduledProducedUnits = scheduledProducedUnits,
                    orderERPStatus = orderERPStatus
                };

                string postData = JsonConvert.SerializeObject(bodyData);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = 30000;

                string authInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("bg:bg957768"));
                request.Headers.Add("Authorization", "Basic " + authInfo);

                byte[] byteData = Encoding.UTF8.GetBytes(postData);
                request.ContentLength = byteData.Length;

                using (Stream requestBody = request.GetRequestStream())
                {
                    requestBody.Write(byteData, 0, byteData.Length);
                }

                using (WebResponse response = request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8))
                        {
                            string responseData = streamReader.ReadToEnd();
                            var result = JsonConvert.DeserializeObject<ApiResponse>(responseData);

                            if (result != null && result.code == 500)
                            {
                                _needBlockOperation = true;
                                _errorMessage = string.IsNullOrEmpty(result.msg) ? "MES接口返回错误，无法下达" : result.msg;
                                return;
                            }

                            if (result != null && result.success)
                            {
                                this.View.ShowMessage("MES接口校验通过，可以下达");
                            }
                            else
                            {
                                this.View.ShowMessage("MES接口返回：" + (result?.msg ?? "未知状态"), MessageBoxType.Error);
                            }
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                _needBlockOperation = true;
                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        string errorResponse = reader.ReadToEnd();
                        try
                        {
                            var errorResult = JsonConvert.DeserializeObject<ApiResponse>(errorResponse);
                            if (errorResult != null && errorResult.code == 500)
                            {
                                _errorMessage = string.IsNullOrEmpty(errorResult.msg) ? "MES接口返回错误，无法下达" : errorResult.msg;
                            }
                            else
                            {
                                _errorMessage = "请求失败：" + errorResponse;
                            }
                        }
                        catch
                        {
                            _errorMessage = "请求失败：" + errorResponse;
                        }
                    }
                }
                else
                {
                    _errorMessage = "网络请求失败：" + ex.Message;
                }

                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
            }
            catch (Exception ex)
            {
                _needBlockOperation = true;
                _errorMessage = "执行MES校验失败：" + ex.Message;
                this.View.ShowMessage(_errorMessage, MessageBoxType.Error);
            }
        }

        private void ExecuteUndoToPlanConfirm()
        {
            try
            {
                DynamicObject dataPacket = this.Model.DataObject;
                string productionOrderCode = dataPacket["BillNo"]?.ToString();

                if (string.IsNullOrEmpty(productionOrderCode))
                {
                    this.View.ShowMessage("无法获取生产订单号，请检查单据", MessageBoxType.Error);
                    return;
                }

                string url = $"http://192.168.1.6:80/iMark/v1/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode?productionOrderCode={productionOrderCode}";

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.ContentType = "application/json";
                request.Accept = "application/json";
                request.Timeout = 30000;

                string authInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes("bg:bg957768"));
                request.Headers.Add("Authorization", "Basic " + authInfo);

                using (WebResponse response = request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        using (StreamReader streamReader = new StreamReader(responseStream, Encoding.UTF8))
                        {
                            string responseData = streamReader.ReadToEnd();
                            dynamic result = JsonConvert.DeserializeObject(responseData);

                            bool hasProcessOrder = false;
                            if (result != null && result.data != null && result.data.Count > 0)
                            {
                                hasProcessOrder = true;
                            }

                            if (hasProcessOrder)
                            {
                                _needBlockOperation = true;
                                _errorMessage = "MES中已存在工序订单，不允许反执行";
                                return;
                            }

                            this.View.ShowMessage("校验通过，可以反执行");
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        string errorResponse = reader.ReadToEnd();
                        this.View.ShowMessage("查询MES失败：" + errorResponse, MessageBoxType.Error);
                    }
                }
                else
                {
                    this.View.ShowMessage("查询MES失败：" + ex.Message, MessageBoxType.Error);
                }
            }
            catch (Exception ex)
            {
                this.View.ShowMessage("执行失败：" + ex.Message, MessageBoxType.Error);
            }
        }

        public class ApiResponse
        {
            public object data { get; set; }
            public object result { get; set; }
            public int code { get; set; }
            public string msg { get; set; }
            public bool success { get; set; }
        }

        #endregion
    }
}