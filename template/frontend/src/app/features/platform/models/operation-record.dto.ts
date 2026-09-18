import { PagedRequestDto } from '../../../shared/models/paged-request.dto';

/** 一条操作记录：什么人、在什么时间、做了什么、结果如何。 */
export interface OperationRecordOutputDto {
  id: string;
  /**
   * 业务动作码（如 `user.created`）。
   *
   * 由下游业务自行定义，**界面按它查词条渲染成一句话**（`operationRecords.actions.<码>`）。
   *
   * 早先这里写的是"原样渲染、不翻译"，理由是"框架不可能预知有哪些码"。那句话对**框架**成立，
   * 对**模板自己登记的动作码**不成立——本项目的码由 `OperationActionDefinitionProvider` 登记，
   * 数量确定、可闸门校验（`check-operation-action-i18n.py`）。
   * 真正预知不了的是下游新增的码，它们按"未登记"降级为原样显示裸码。
   */
  action: string;
  /** 操作目标标识；没有目标时为 `-`。 */
  targetId: string;

  /**
   * 操作目标的人类可读名**快照**；取不到时为空。
   *
   * 授权阶段被拒的记录没有名字，这是**正确**的：那条路径上调用方正因无权访问该目标而被拒，
   * 回填名字等于把他无权查看的内容写进了他能读到的记录。界面据此降级显示标识。
   */
  targetName?: string;
  /** 授权依据：权限名，或业务自己的标记。 */
  authorizationBasis: string;
  /** `Succeeded` 或 `Failed`。 */
  outcome: string;
  creationTime: string;
  /** 操作人标识；机器主体为 `client:<client_id>`，后台作业由宿主自定前缀。 */
  actorId?: string;
  /** 操作人显示名的快照——当时的名字，不随后来改名而变。 */
  actorName?: string;
  /**
   * 这条记录的主体是否就是 `targetName` 本人；操作人缺失时据此回落到目标名。
   *
   * **由服务端判定，前端只读。** 登录这类自证动作发生在认证之前，请求主体当时确实是匿名的，
   * `actorId` 为空是诚实的记录；但显示成"匿名"没人读得懂——"什么人"由目标承载着。
   * 判定不放在前端，一来会把服务端的动作登记复制一份过来、两处迟早漂移，
   * 二来 `auth.login.failed` 的目标是调用方提交的用户名、未经验证，
   * 让它回落就等于让任何人都能往审计界面的操作人列里写文本。
   */
  actorIsTarget?: boolean;
  /**
   * 模拟登录时的**真实**操作人显示名。
   *
   * 有值即表示这次操作是宿主管理员以租户身份做的：`actorName` 是被模拟的租户用户，
   * 真正按下按钮的是这里的人。两者要一起显示，否则追责会指向一个什么都没做的人。
   */
  impersonatorName?: string;
  /**
   * 失败原因的错误码，界面按它本地化；成功时为空。
   *
   * 存码而不是渲染好的句子：写入时是哪国语言，此后所有读者看到的就是哪国语言，改不回来。
   */
  failureCode?: string;

  /** 失败原因的本地化占位参数（JSON 对象字符串）。 */
  failureData?: string;

  /**
   * 面向排查的技术说明；**仅宿主可见**，租户读者拿到的恒为空。
   *
   * 裁剪在服务端完成——前端不显示不等于没下发。
   */
  failureDetail?: string;

  /**
   * 链路标识，用于按它去日志里查这次调用的完整细节；**仅宿主可见**。
   */
  correlationId?: string;

  /**
   * 操作发生时的租户；**仅宿主可见**，宿主上下文里的操作为空。
   *
   * 租户用户调用宿主接口被拒时，记录落在宿主这边，靠它回答"是哪个租户的人"。
   */
  actorTenantId?: string;
}

/**
 * 操作记录查询入参。
 *
 * **没有排序参数**：后端契约是 offset/limit/keyword 加时间区间，不含排序。这张表只有一种
 * 有意义的读法——按时间倒序看最近发生了什么；给一个能改排序的入口，只会让人翻出一页"最早的几条"。
 *
 * 因此这里把基类的 `sorting` 去掉，而不是留着不填：留着就是一个传了也不起作用的字段，
 * 调用方无从知道它会被悄悄丢掉。
 */
export interface GetOperationRecordsInputDto extends Omit<PagedRequestDto, 'sorting'> {
  /** 关键字，同时匹配动作码、目标标识与操作人名。 */
  keyword?: string;
  /**
   * 起始时刻（**含**），ISO 8601 **UTC** 字符串。
   *
   * 记录落库的是 UTC 时刻，而界面上选的是展示时区里的日期，两者必须由调用方换算后再传，
   * 否则整段区间会按部署地偏移——那种错不会报错，只表现为"某些记录莫名不在范围里"。
   */
  startTime?: string;
  /**
   * 结束时刻（**含**），ISO 8601 **UTC** 字符串。
   *
   * 选到某一天时要取到那天的 23:59:59.999 再换算，否则当天的记录会整天看不见。
   */
  endTime?: string;

  /**
   * 按类别筛选（后端展开成动作码后再查）。
   *
   * 服务端用 `List<string>` 接收，查询串里是**重复键**（`?categories=a&categories=b`）——
   * 服务层必须逐个 `append` 而不是 `set`，后者会覆盖、只传最后一个值而不报错。
   */
  categories?: string[];

  /** 按动作码筛选；与 {@link categories} 同时给出时取交集（维度之间是 AND）。 */
  actions?: string[];

  /** 按结果筛选：`Succeeded` 或 `Failed`；非法值会被后端以 400 拒绝。 */
  outcome?: string;
}

/**
 * 导出入参：与查询用同一组筛选字段，但**没有分页**。
 *
 * 不继承 {@link GetOperationRecordsInputDto}：那个带 `offset`／`limit`，
 * 而导出取的是"筛选结果的前 N 条"，给一个偏移量只会让人以为能靠翻页拼出全量。
 * `limit` 的上限由后端 `ExportOperationRecordsInputDto.MaximumExportCount` 封顶（10000），
 * 超出会被以 400 拒绝。**"导出全部"本接口做不到**，那需要异步导出子系统。
 */
export interface ExportOperationRecordsInputDto {
  keyword?: string;
  startTime?: string;
  endTime?: string;
  categories?: string[];
  actions?: string[];
  outcome?: string;
  /** 导出条数；不传则由后端取上限。 */
  limit?: number;
}

/** 筛选项：类别与动作由服务端下发，界面不硬编码动作码。 */
export interface OperationRecordFilterOptionsDto {
  /** 出现过的类别。 */
  categories: string[];
  /** 可选的动作，含所属类别与严重度。 */
  actions: OperationActionOptionDto[];
}

/** 一个可选的操作动作。 */
export interface OperationActionOptionDto {
  /** 动作码，界面按它查句子模板。 */
  code: string;
  /** 所属类别，用于按类别联动。 */
  category: string;
  /** 严重度：`Info` / `Notice` / `Critical`。 */
  severity: string;
}
