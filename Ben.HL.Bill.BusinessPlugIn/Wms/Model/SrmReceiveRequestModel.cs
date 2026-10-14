using System.Collections.Generic;

namespace Ben.HL.Bill.BusinessPlugIn
{
    public class SrmReceiveRequestModel
    {
        public string groupCode { get; set; }
        public string ERPDoc { get; set; }
        public string userCode { get; set; }
        public string departmentCode { get; set; }
        public string docDate { get; set; }
        public string supCode { get; set; }
        public string supNamess { get; set; }
        public List<SrmReceiveEntryModel> srtRtfList { get; set; } = new List<SrmReceiveEntryModel>();
    }

    public class SrmReceiveEntryModel
    {
        public int docSN { get; set; }
        public string recieveDoc { get; set; }
        public int recieveDocSN { get; set; }
        public string srmrecieveDocSN { get; set; }
        public string materialCode { get; set; }
        public string materialName { get; set; }
        public string materialSpec { get; set; }
        public string unitCode { get; set; }
        public string batchNo { get; set; }
        public decimal num { get; set; }
        public decimal? cartonSize { get; set; }
        public decimal? cartonNum { get; set; }
        public string warehouseCode { get; set; }
        public string locationCode { get; set; }
    }
}
