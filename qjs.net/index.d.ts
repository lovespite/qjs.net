/**
 * 异步文件系统模块，所有的文件操作均返回 Promise 并由内置 EventLoop 调度。
 * @namespace
 */
declare const fsAsync: {
    /**
     * 异步读取整个文件的文本内容。
     * @param {string} path - 目标文件路径。
     * @param {string} [encoding="utf-8"] - 文本编码（默认: "utf-8"）。
     * @returns {Promise<string>} 包含文件文本内容的 Promise。
     */
    readFile(path: string, encoding?: string): Promise<string>;

    /**
     * 异步读取整个文件的二进制内容。
     * @param {string} path - 目标文件路径。
     * @returns {Promise<ArrayBuffer>} 包含文件二进制数据的 Promise。
     */
    readFileBytes(path: string): Promise<ArrayBuffer>;

    /**
     * 异步将文本内容写入文件，如果文件不存在则自动创建，如果存在则覆盖。
     * @param {string} path - 目标文件路径。
     * @param {string} content - 要写入的文本内容。
     * @param {string} [encoding="utf-8"] - 文本编码（默认: "utf-8"）。
     * @returns {Promise<boolean>} 写入成功返回 true 的 Promise。
     */
    writeFile(path: string, content: string, encoding?: string): Promise<boolean>;

    /**
     * 异步将二进制数据写入文件。
     * @param {string} path - 目标文件路径。
     * @param {ArrayBufferView | ArrayBuffer} buffer - 要写入的二进制数据（如 Uint8Array 或 ArrayBuffer）。
     * @returns {Promise<boolean>} 写入成功返回 true 的 Promise。
     */
    writeFileBytes(path: string, buffer: ArrayBufferView | ArrayBuffer): Promise<boolean>;

    /**
     * 异步将文本追加到文件末尾，如果文件不存在则自动创建。
     * @param {string} path - 目标文件路径。
     * @param {string} content - 要追加的文本内容。
     * @param {string} [encoding="utf-8"] - 文本编码（默认: "utf-8"）。
     * @returns {Promise<boolean>} 追加成功返回 true 的 Promise。
     */
    appendFile(path: string, content: string, encoding?: string): Promise<boolean>;

    /**
     * 异步检查文件或目录是否存在。
     * @param {string} path - 目标路径。
     * @returns {Promise<boolean>} 如果存在则返回 true，否则返回 false。
     */
    exists(path: string): Promise<boolean>;

    /**
     * 异步检查目标路径是否是一个文件。
     * @param {string} path - 目标路径。
     * @returns {Promise<boolean>} 如果是文件且存在则返回 true。
     */
    isFile(path: string): Promise<boolean>;

    /**
     * 异步检查目标路径是否是一个目录。
     * @param {string} path - 目标路径。
     * @returns {Promise<boolean>} 如果是目录且存在则返回 true。
     */
    isDirectory(path: string): Promise<boolean>;

    /**
     * 异步删除文件或目录（如果为目录，则进行递归删除）。
     * @param {string} path - 要删除的文件或目录路径。
     * @returns {Promise<boolean>} 删除成功返回 true，路径不存在返回 false。
     */
    remove(path: string): Promise<boolean>;

    /**
     * 异步重命名/移动文件或目录。
     * @param {string} oldPath - 原始路径。
     * @param {string} newPath - 目标路径。
     * @returns {Promise<boolean>} 重命名成功返回 true。
     */
    rename(oldPath: string, newPath: string): Promise<boolean>;

    /**
     * 异步复制文件。如果目标位置已有文件则将被覆盖。
     * @param {string} src - 源文件路径。
     * @param {string} dst - 目标文件路径。
     * @returns {Promise<boolean>} 复制成功返回 true。
     */
    copy(src: string, dst: string): Promise<boolean>;

    /**
     * 异步创建目录。
     * @param {string} path - 目录路径。
     * @returns {Promise<boolean>} 目录创建成功（或已存在）返回 true。
     */
    mkdir(path: string): Promise<boolean>;

    /**
     * 异步读取目录中的文件和子目录列表。
     * @param {string} path - 目标目录路径。
     * @returns {Promise<string[]>} 包含目录内各项名称的数组。
     */
    readDir(path: string): Promise<string[]>;

    /**
     * 异步获取文件或目录的状态信息。
     * @param {string} path - 目标路径。
     * @returns {Promise<{
     *   size: number,
     *   isFile: boolean,
     *   isDirectory: boolean,
     *   created: string,
     *   modified: string,
     *   accessed: string
     * } | null>} 状态信息对象，文件/目录不存在时返回 null。
     */
    stat(path: string): Promise<{ size: number; isFile: boolean; isDirectory: boolean; created: string; modified: string; accessed: string; } | null>;

    /**
     * 异步获取当前工作目录。
     * @returns {Promise<string>} 当前工作目录的完整路径。
     */
    getcwd(): Promise<string>;

    /**
     * 异步获取系统临时目录路径。
     */
    tempDir(): Promise<string>;

    /** 
     * 异步弹出文件选择对话框，允许用户选择一个或多个文件，并返回所选文件的路径。
     * @param options
     */
    getOpenFileName(options?: { title?: string; initialDir?: string; filter?: string, multiSelect?: boolean }): Promise<string | null>;

    /**
     * 异步弹出文件保存对话框，允许用户选择一个文件路径用于保存，并返回所选路径。
     * @param options
     */
    getSaveFileName(options?: { title?: string; initialDir?: string; filter?: string }): Promise<string | null>;
};

/**
* QuickJS 引擎内定制的 Fetch 模块配置选项。
*/
interface FetchOptions {
    /** HTTP 请求方法，例如 "GET", "POST", "PUT", "DELETE" 等。（默认："GET"） */
    method?: string;

    /**
     * HTTP 请求头，需要是键值对的对象。
     * 例如：{ "Content-Type": "application/json", "Authorization": "Bearer token" }
     */
    headers?: Record<string, string>;

    /** 请求的 Payload 数据（发送请求的主体）。通常配合 POST 或 PUT 使用。 */
    body?: string;

    /** 该次请求的超时时间，单位为毫秒。（默认使用模块安装时的配置，一般是 30000ms） */
    timeout?: number;

    /**
     * 为此请求覆盖或指定使用的代理 URL。
     * 例如："http://127.0.0.1:7890" 或 "socks5://127.0.0.1:1080"
     */
    proxy?: string;

    /** 是否在此请求中忽略 SSL/TLS 证书错误（例如对于自签名证书）。 */
    ignoreSslErrors?: boolean;
}

/**
 * QuickJS 引擎返回的定制化响应对象。
 */
interface FetchResponse {
    /** 标示请求是否执行成功，对应 HTTP 状态码 200–299 为 true，否则为 false。 */
    ok: boolean;

    /** HTTP 状态响应码（例如 200, 404, 500）。 */
    status: number;

    /** 状态文本描述（例如 "OK", "Not Found"）。 */
    statusText: string;

    /**
     * 返回的 HTTP 标头键值对对象。
     * 响应头与内容头都被平铺在此对象中。
     */
    headers: Record<string, string>;

    /** 当前发出请求的原始 URL。 */
    url: string;

    /** 服务器返回的原始响应文本数据。 */
    text(): Promise<string>;

    /**
     * 辅助方法：将响应的 `text` 内容作为 JSON 对象进行解析并返回。
     * @returns {any} 解析后的 JSON 对象。
     */
    json(): Promise<any>;

    /**
     * 辅助方法：将响应的 `text` 内容编码转换并返回其二进制 `ArrayBuffer` 格式数据。
     * @returns {ArrayBuffer} 响应内容的二进制数据。
     */
    arrayBuffer(): Promise<ArrayBuffer>;
}

/**
 * 在 QuickJS 引擎内部全局注册的异步 `fetch` 网络请求方法。
 * 与浏览器原生的 fetch API 不完全一致，它是一个简化并根据 C# HttpClient 封装的版本。
 *
 * @param {string} url - 目标请求的绝对 URL（仅支持 http 和 https）。
 * @param {FetchOptions} [options] - 可选的请求配置选项。
 * @returns {Promise<FetchResponse>} 返回一个 Promise，成功后解析为 `FetchResponse`。
 */
declare function fetch(url: string, options?: FetchOptions): Promise<FetchResponse>;
/**
 * 将字符串编码为 UTF-8 的二进制数据
 */
//@ts-ignore
declare class TextEncoder {
    constructor();
    /** 获取编码格式，始终为 "utf-8" */
    readonly encoding: string;
    /** 将字符串编码为 Uint8Array */
    encode(str?: string): Uint8Array;
}

/**
 * 将 UTF-8 的二进制数据解码为字符串
 */
//@ts-ignore
declare class TextDecoder {
    constructor(label?: string);
    /** 获取解码格式，通常为 "utf-8" */
    readonly encoding: string;
    /** 将 BufferSource 解码为字符串 */
    decode(buffer?: ArrayBuffer | ArrayBufferView): string;
}

/**
 * 子菜单项接口，表示一个菜单项的标题和对应的操作标识符。
 */
declare interface SubmenuItem {
    /** 显示在菜单上的文字 */
    title: string;
    /** 选择此菜单项时触发的操作标识符，供插件内部使用以区分不同菜单项的功能 */
    action?: string;
    /** 菜单项类型，默认为 "normal"。可以是 "separator"（分隔线）、"checkbox"（复选框）。 */
    type: "normal" | "separator" | "checkbox";
    /** 仅当 type 为 "checkbox" 或 "radio" 时有效，表示该项是否被选中。 */
    checked?: boolean;
    /** 快捷键字符串，例如 "Ctrl+Shift+Alt+S"，用于为菜单项添加快捷键。 */
    shortcut?: string;
    children?: SubmenuItem[]; // 可选的子菜单项数组，用于创建多级菜单结构
}

/**
 * 显示配置选项
 */
declare interface ToastOptions {
    /** 提示级别，默认为 'info'。 */
    level?: 'info' | 'success' | 'warn' | 'error';
    /** 提示持续时间（毫秒），默认为 5000。
     * 设置为 0 或负数表示提示将一直显示，直到用户手动关闭或调用 dismiss 方法。
     */
    durationMs?: number;
    /** 是否在点击时关闭提示，默认为 false。 */
    dismissOnClick?: boolean;
}

/**
 * 全局 Toast 提示对象，用于在界面上显示短暂的通知。
 */
declare const Toast: {
    /**
     * 显示带有自定义选项的 Toast 提示。
     * @param msg 提示内容
     * @param options 选项配置
     * @returns Toast 实例的 ID（如果有返回）
     */
    show(msg: string, options?: ToastOptions): string | void;

    /**
     * 显示普通的 info 级别提示。
     * @param msg 提示内容
     * @param durationMs 持续时间（毫秒），默认 5000
     * @returns Toast 实例的 ID（如果有返回）
     */
    info(msg: string, durationMs?: number): string | void;

    /**
     * 显示成功级别的提示。
     * @param msg 提示内容
     * @param durationMs 持续时间（毫秒），默认 5000
     * @returns Toast 实例的 ID（如果有返回）
     */
    success(msg: string, durationMs?: number): string | void;

    /**
     * 显示警告级别的提示。
     * @param msg 提示内容
     * @param durationMs 持续时间（毫秒），默认 5000
     * @returns Toast 实例的 ID（如果有返回）
     */
    warn(msg: string, durationMs?: number): string | void;

    /**
     * 显示错误级别的提示。
     * @param msg 提示内容
     * @param durationMs 持续时间（毫秒），默认 5000
     * @returns Toast 实例的 ID（如果有返回）
     */
    error(msg: string, durationMs?: number): string | void;

    /**
     * 隐藏指定的 Toast 提示。
     * @param id 要隐藏的 Toast ID
     */
    dismiss(id: string): void;
}