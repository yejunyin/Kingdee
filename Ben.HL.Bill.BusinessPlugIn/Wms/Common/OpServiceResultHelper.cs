using System;
using System.Collections.Generic;
using Kingdee.BOS;
using Kingdee.BOS.Core.DynamicForm;

namespace Ben.HL.Bill.BusinessPlugIn
{
    /// <summary>
    /// 操作服务插件（AbstractOperationServicePlugIn）结果弹窗公共辅助类。
    /// AbstractOperationServicePlugIn 基类没有 View 属性，
    /// 只能通过 OperationResult.CustomMessageModel 向金蝶客户端传递弹窗消息。
    /// 本类封装该机制，供所有操作服务插件复用。
    /// </summary>
    public static class OpServiceResultHelper
    {
        /// <summary>
        /// 通过 OperationResult 向金蝶客户端弹出自定义汇总消息窗口。
        /// 若 operationResult 为 null 或未传任何消息，直接跳过（API 后台调用场景自然无弹窗）。
        /// </summary>
        /// <param name="operationResult">操作服务插件的 this.OperationResult</param>
        /// <param name="context">操作服务插件的 this.Context</param>
        /// <param name="title">弹窗标题（如 "生产汇报单推送WMS结果"）</param>
        /// <param name="summaryMessage">顶部汇总描述（如 "成功 3 张，失败 1 张，共 4 张"）</param>
        /// <param name="messages">逐条详情列表，每行一条</param>
        public static void ShowOperationResult(
            IOperationResult operationResult,
            Context context,
            string title,
            string summaryMessage,
            List<string> messages)
        {
            if (operationResult == null || messages == null || messages.Count == 0) return;

            try
            {
                var model = K3DisplayerModel.Create(context, title);
                model.SummaryMessage = summaryMessage;
                foreach (var msg in messages)
                {
                    model.AddMessage(msg);
                }

                operationResult.IsShowMessage = true;
                operationResult.CustomMessageModel = model;
            }
            catch (Exception)
            {
                // 弹窗异常不得阻断主流程
            }
        }
    }
}
