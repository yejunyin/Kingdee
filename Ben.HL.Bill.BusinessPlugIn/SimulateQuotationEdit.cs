using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingdee.BOS;
using Kingdee.BOS.Core.Bill.PlugIn;
using Kingdee.BOS.Core.DynamicForm.PlugIn.Args;
using Kingdee.BOS.Orm.DataEntity;
using Kingdee.BOS.Core.SqlBuilder;
using Kingdee.BOS.Core.Metadata;
using System.ComponentModel;
using Kingdee.BOS.App.Data;
using Kingdee.BOS.Core.Metadata.EntityElement;
using Kingdee.BOS.Util;
using Kingdee.BOS.ServiceHelper;
using System.Data;
using Kingdee.BOS.Core;

namespace Ben.HL.Bill.BusinessPlugIn
{
    [Kingdee.BOS.Util.HotUpdate]
    [Description("Ben模拟报价单表单插件")]
    public class SimulateQuotationEdit : AbstractBillPlugIn
    {
        /// <summary>
        /// 初始化，对其他界面传来的参数进行处理，对控件某些属性进行处理
        /// 这里不宜对数据DataModel进行处理
        /// </summary>
        /// <param name="e"></param>
        public override void OnInitialize(InitializeEventArgs e)
        {

        }

        /// <summary>
        /// 新建单据加载数据完成之后，需要处理的功能
        /// </summary>
        /// <param name="e"></param>
        public override void AfterCreateNewData(EventArgs e)
        {

        }

        /// <summary>
        /// 修改，查看单据加载已有数据之后，需要处理的功能
        /// </summary>
        /// <param name="e"></param>
        public override void AfterLoadData(EventArgs e)
        {

        }

        /// <summary>
        /// 数据加载之后，需要处理的功能，这里主要对界面样式进行处理，尽量不要对Datamodel进行处理
        /// </summary>
        /// <param name="e"></param>
        public override void AfterBindData(EventArgs e)
        {
            EntryEntity entryEntity = this.View.BusinessInfo.GetEntryEntity("FEntity");
            DynamicObjectCollection parentmateriallist = this.View.Model.GetEntityDataObject(entryEntity);
            //IList<string> parentcode = parentmateriallist.Select(p => Convert.ToString(p["FMaterialID"])).ToList();
            string FDATE = this.View.Model.GetValue("FDATE").ToString();
            int i = 0;
            foreach (DynamicObject rowObj in parentmateriallist)
            {
                this.Model.SetEntryCurrentRowIndex("FEntity", i);
                EntryEntity entryDetailEntity = this.View.BusinessInfo.GetEntryEntity("FDetailEntity");
                DynamicObjectCollection detailmateriallist = this.View.Model.GetEntityDataObject(entryDetailEntity);
                int j = 0;
                foreach (DynamicObject detailrowObj in detailmateriallist)
                {
                    string FCPERPRICE = detailrowObj["FCPERPRICE"].ToString();
                    if (FCPERPRICE == null || FCPERPRICE == "")
                    {
                        string detailmaterialId = detailrowObj["FCMatlId_Id"].ToString();
                        string sqlstr = $"select FPRICE from PVCE_t_Cust100013 t1 inner join PVCE_t_Cust_Entry100019 as t2 on t1.FID=t2.FID where FDOCUMENTSTATUS='C'and FMATERIALID={detailmaterialId} and FEFFECTIVEDATE<='{FDATE}' and FEXPIRYDATE>='{FDATE}' order by FEFFECTIVEDATE desc";
                        DynamicObjectCollection detailobjects = DBUtils.ExecuteDynamicObject(this.Context, sqlstr);
                        if (detailobjects.Count > 0)
                        {
                            //decimal FCPerPrice = Convert.ToDecimal(detailrowObj["FCPerPrice"]);
                            decimal CPerPrice = Convert.ToDecimal(detailobjects[0]["FPRICE"]); //+ FCPerPrice;
                                                                                               //detailrowObj["FCPerPrice"] = Convert.ToDecimal(detailobjects[0]["FPRICE"]);
                            this.View.Model.SetValue("FCPerPrice", CPerPrice, j);
                            this.View.InvokeFieldUpdateService("FCPriceUnitQty", j);
                            //this.View.Model.SetValue("FCPerPrice", CPerPrice, j);
                        }
                    }
                    j++;
                }
                string parentmaterialId = rowObj["FMaterialId_Id"].ToString();
                string sqlstr2 = $"select FPRICE from PVCE_t_Cust100013 t1 inner join PVCE_t_Cust_Entry100019 as t2 on t1.FID=t2.FID where FDOCUMENTSTATUS='C'and  FMATERIALID={parentmaterialId} and FEFFECTIVEDATE<='{FDATE}' and FEXPIRYDATE>='{FDATE}' order by FEFFECTIVEDATE desc";
                DynamicObjectCollection objects = DBUtils.ExecuteDynamicObject(this.Context, sqlstr2);
                if (objects.Count > 0)
                {
                    //rowObj["F_hll_Price"] = objects[0]["FPRICE"];
                    decimal FQuoteCost = Convert.ToDecimal(rowObj["FQuoteCost"]);
                    decimal FPRICE = Convert.ToDecimal(objects[0]["FPRICE"]);
                    this.View.Model.SetValue("F_hll_Price", FPRICE, i);
                    this.View.Model.SetValue("FQuoteCost", FPRICE + FQuoteCost, i);
                }
                i++;
            }
            this.View.UpdateView("FDetailEntity");
        }
    }


}
