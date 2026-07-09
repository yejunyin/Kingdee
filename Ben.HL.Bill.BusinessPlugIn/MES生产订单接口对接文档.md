# MES接口对接文档

## 文档概述

本文档描述了金蝶ERP系统与MES系统之间的数据交互接口，用于生产订单的下达校验和反执行校验。

**版本**: V1.0  
**更新日期**: 2026-06-16  
**接口基础地址**: `http://192.168.*.*:80/iMark/v1`

---

## 通用说明

### 认证方式

所有接口均采用 **Basic Authentication** 认证方式。

**请求头配置**:
```
Authorization: Basic **:**
```

**认证凭据**:
- 用户名: `**`
- 密码: `**`

### 通用请求头

| Header名称 | 值 | 说明 |
|-----------|-----|------|
| Content-Type | application/json | 请求内容类型 |
| Accept | application/json | 响应内容类型 |

### 超时设置

- **请求超时时间**: 30000ms (30秒)

---

## 接口列表

### 1. 生产订单下达校验接口

#### 基本信息

| 项目 | 内容 |
|------|------|
| **接口名称** | 生产订单下达校验 |
| **接口URL** | `/DBProductionOrderInfo/modifyIfNoProcessOrder` |
| **请求方式** | `POST` |
| **Content-Type** | `application/json` |
| **功能描述** | 在ERP下达生产订单前，向MES推送订单数据并校验是否允许下达。如果MES返回错误码500，则阻止下达操作 |

#### 业务流程

```
ERP系统 → [点击"下达"按钮] → 调用本接口 → MES校验 → 返回结果 → ERP决定是否继续下达
```

#### 请求参数

| 参数名 | 类型 | 必填 | 说明 | 示例值 |
|--------|------|------|------|--------|
| productionOrderCode | string | 是 | 生产订单号（ERP单据编号） | "PO20260616001" |
| scheduledProducedUnits | int | 是 | 计划生产数量 | 100 |
| orderERPStatus | string | 是 | ERP订单状态 | "下达" |

#### 请求示例

```json
{
    "productionOrderCode": "PO20260616001",
    "scheduledProducedUnits": 100,
    "orderERPStatus": "下达"
}
```

#### 响应参数

| 参数名 | 类型 | 说明 |
|--------|------|------|
| code | int | 状态码，200表示成功，500表示失败 |
| msg | string | 返回消息 |
| success | bool | 是否成功 |
| data | object | 返回数据（可选） |
| result | object | 结果对象（可选） |

#### 响应示例

**成功响应** (code=200):
```json
{
    "code": 200,
    "msg": "操作成功",
    "success": true,
    "data": null
}
```

**失败响应** (code=500):
```json
{
    "code": 500,
    "msg": "MES中已存在工序订单，不允许下达",
    "success": false,
    "data": null
}
```

#### 错误码说明

| 错误码 | 说明 | 处理方式 |
|--------|------|----------|
| 200 | 校验通过，允许下达 | 继续执行下达操作 |
| 500 | 校验不通过，阻止下达 | 显示错误消息，终止操作 |

#### ERP端处理逻辑

1. 调用接口发送生产订单数据
2. 接收MES响应
3. **如果 code == 500**：
   - 设置阻止标志 `_needBlockOperation = true`
   - 记录错误消息 `_errorMessage = result.msg`
   - 在 `BeforeDoOperation` 事件中取消操作 (`e.Cancel = true`)
   - 向用户显示错误提示
4. **如果 code == 200 且 success == true**：
   - 显示成功消息："MES接口校验通过，可以下达"
   - 允许继续执行下达操作

---

### 2. 工序订单查询接口（反执行校验）

#### 基本信息

| 项目 | 内容 |
|------|------|
| **接口名称** | 工序订单信息查询 |
| **接口URL** | `/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode` |
| **请求方式** | `GET` |
| **Content-Type** | application/json |
| **功能描述** | 根据生产订单号查询MES中的工序订单信息，用于判断是否允许反执行（撤销下达）操作 |

#### 业务流程

```
ERP系统 → [点击"反执行"按钮] → 调用本接口 → 查询工序订单 → 判断结果 → ERP决定是否允许反执行
```

#### 请求参数

| 参数名 | 类型 | 必填 | 说明 | 位置 | 示例值 |
|--------|------|------|------|------|--------|
| productionOrderCode | string | 是 | 生产订单号 | Query参数 | "PO20260616001" |

#### 请求示例

```
GET /iMark/v1/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode?productionOrderCode=PO20260616001
```

**完整请求URL**:
```
http://192.168.*.*:80/iMark/v1/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode?productionOrderCode=PO20260616001
```

#### 响应参数

| 参数名 | 类型 | 说明 |
|--------|------|------|
| code | int | 状态码 |
| msg | string | 返回消息 |
| success | bool | 是否成功 |
| data | array/object | 工序订单列表或详情 |

#### 响应示例

**存在工序订单** (不允许反执行):
```json
{
    "code": 200,
    "msg": "查询成功",
    "success": true,
    "data": [
        {
            "processOrderId": "PROC001",
            "productionOrderCode": "PO20260616001",
            "status": "进行中"
        },
        {
            "processOrderId": "PROC002", 
            "productionOrderCode": "PO20260616001",
            "status": "已完成"
        }
    ]
}
```

**无工序订单** (允许反执行):
```json
{
    "code": 200,
    "msg": "查询成功",
    "success": true,
    "data": []
}
```

#### ERP端处理逻辑

1. 根据当前单据的生产订单号调用接口
2. 接收MES响应
3. **判断 data 是否有数据**：
   - **如果 data 不为空且 Count > 0**（存在工序订单）：
     - 设置阻止标志 `_needBlockOperation = true`
     - 错误消息：`"MES中已存在工序订单，不允许反执行"`
     - 阻止反执行操作
   - **如果 data 为空或不存在**（无工序订单）：
     - 显示成功消息："校验通过，可以反执行"
     - 允许继续执行反执行操作

---

## 异常处理

### 网络异常

当发生网络异常时，ERP系统会捕获异常并进行如下处理：

| 异常类型 | 处理方式 |
|----------|----------|
| WebException (HTTP错误) | 读取错误响应体，尝试解析JSON错误信息，显示给用户 |
| WebException (无响应) | 显示网络连接错误消息 |
| 其他Exception | 显示通用错误消息，包含异常详细信息 |

### 异常处理示例代码

```csharp
catch (WebException ex)
{
    if (ex.Response != null)
    {
        using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
        {
            string errorResponse = reader.ReadToEnd();
            // 解析并显示错误信息
            this.View.ShowMessage("请求失败：" + errorResponse, MessageBoxType.Error);
        }
    }
    else
    {
        this.View.ShowMessage("网络请求失败：" + ex.Message, MessageBoxType.Error);
    }
}
catch (Exception ex)
{
    this.View.ShowMessage("执行失败：" + ex.Message, MessageBoxType.Error);
}
```

---

## 接口调用时序图

### 下达操作时序

```
用户 → ERP插件 → MES接口 → ERP决策
 │       │         │          │
 │  点击下达按钮   │          │
 │──────────────→│          │
 │               │ POST请求  │
 │               │─────────→│
 │               │          │
 │               │  响应结果 │
 │               │←─────────│
 │               │          │
 │               │ 判断code  │
 │               │─────────→│
 │               │          │
 │       显示结果 ←──────────│
 │←──────────────│          │
```

### 反执行操作时序

```
用户 → ERP插件 → MES接口 → ERP决策
 │       │         │          │
 │ 点击反执行按钮  │          │
 │──────────────→│          │
 │               │ GET请求   │
 │               │─────────→│
 │               │          │
 │               │  响应结果 │
 │               │←─────────│
 │               │          │
 │              判断data     │
 │               │─────────→│
 │               │          │
 │       显示结果 ←──────────│
 │←──────────────│          │
```

---

## 技术要求

### 服务端要求 (MES)

- 支持HTTP/1.1协议
- 支持Basic Authentication认证
- 支持JSON格式的请求和响应
- 接口响应时间 < 30秒
- 提供明确的错误码和错误消息

### 客户端要求 (ERP插件)

- .NET Framework 4.5+
- 使用HttpWebRequest进行HTTP通信
- 使用Newtonsoft.Json进行JSON序列化/反序列化
- 支持异步操作（可选优化）

---

## 联系方式

如有接口对接问题，请联系：
- **技术负责人**: Ben
- **邮箱**: yejunyin@gmail.com
- **电话**: 17812345678

---

## 变更记录

| 版本 | 日期 | 修改人 | 修改内容 |
|------|------|--------|----------|
| V1.0 | 2026-06-16 | 系统生成 | 初始版本，包含下达校验和反执行校验两个接口 |

---

## 附录

### A. 完整请求头示例

**POST请求**:
```
POST /iMark/v1/DBProductionOrderInfo/modifyIfNoProcessOrder HTTP/1.1
Host: 192.168.*.*:80
Content-Type: application/json
Accept: application/json
Authorization: Basic ********
Content-Length: [长度]

{请求体JSON}
```

**GET请求**:
```
GET /iMark/v1/DBProcessOrderInfo/getDBProcessOrderInfoListByProductionOrderCode?productionOrderCode=PO20260616001 HTTP/1.1
Host: 192.168.*.*:80
Content-Type: application/json
Accept: application/json
Authorization: Basic ********
```

### B. 数据字典

| 字段 | 英文名 | 类型 | 说明 | 取值范围 |
|------|--------|------|------|----------|
| 生产订单号 | productionOrderCode | string | ERP单据唯一标识 | 非空字符串 |
| 计划生产数量 | scheduledProducedUnits | int | 计划生产的数量 | 正整数 |
| 订单状态 | orderERPStatus | string | ERP端订单状态 | "下达"、"计划"等 |
| 工序订单ID | processOrderId | string | MES端工序订单标识 | MES系统生成 |
