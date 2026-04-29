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
    stat(path: string): Promise<{
        size: number;
        isFile: boolean;
        isDirectory: boolean;
        created: string;
        modified: string;
        accessed: string;
    } | null>;

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
    getOpenFileName(options?: {
        title?: string;
        initialDir?: string;
        filter?: string,
        multiSelect?: boolean
    }): Promise<string | null>;

    /**
     * 异步弹出文件保存对话框，允许用户选择一个文件路径用于保存，并返回所选路径。
     * @param options
     */
    getSaveFileName(options?: { title?: string; initialDir?: string; filter?: string }): Promise<string | null>;


    writeFileBytes(path: string, buffer: Buffer | ArrayBufferView | ArrayBuffer): Promise<boolean>;
    openRead(path: string): Promise<Stream>;
    openWrite(path: string, append?: boolean): Promise<Stream>;
    openDir(path: string): Promise<DirectoryStream>;
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

    /**
     * 以流式方式获取响应体。返回的 Stream 拥有底层 HTTP 响应——一旦调用 body()，
     * 同一响应上的 text() / json() / arrayBuffer() 将不可再用，反之亦然。
     * 适合下载大文件并 pipe 到 fs.openWrite()。
     */
    body(): Stream;
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

// ═══════════════════════════════════════════════════════════════════════════
// Buffer / Stream / DirectoryStream — 流式 IO 与可变字节缓冲区
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Node.js 风格的可变字节缓冲区。底层为 Memory<byte>；slice 共享底层内存
 * 不复制；toUint8Array / toArrayBuffer 会拷贝。
 *
 * 注意：**不支持 `buf[i]` 索引访问**，请使用 `get(i)` / `set(i, v)`。
 */
declare class Buffer {
    private constructor();

    /** 字节长度。 */
    readonly length: number;

    /** 分配 size 字节的新缓冲区，可选用 fill 填充（默认 0）。 */
    static alloc(size: number, fill?: number): Buffer;

    /**
     * 从字符串 / ArrayBuffer / Uint8Array / 另一个 Buffer 构造。
     * 对字符串支持的 encoding：utf8/utf-8（默认）、ascii、latin1/binary、
     * utf16/utf-16le、base64、hex。
     */
    static from(source: string | ArrayBuffer | ArrayBufferView | Buffer, encoding?: string): Buffer;

    /**
     * 拼接多个 Buffer，可选指定结果总长度（不足以 0 填充，超出截断）。
     * 不传或传入非正数 totalLength 时按各段长度求和。
     */
    static concat(list: Buffer[], totalLength?: number): Buffer;

    /** 读取索引 i 处的无符号字节（越界抛错）。 */
    get(i: number): number;

    /** 写入索引 i 处的字节（仅取低 8 位，越界抛错）。 */
    set(i: number, v: number): void;

    /** 返回共享底层内存的子视图（不拷贝）。end 缺省为 length。 */
    slice(start?: number, end?: number): Buffer;

    /** 复制本 buffer 到目标 buffer，返回实际复制的字节数。 */
    copy(target: Buffer, targetStart?: number, sourceStart?: number, sourceEnd?: number): number;

    /** 用 value 填充 [start, end)。 */
    fill(value: number, start?: number, end?: number): void;

    /** 字节级相等比较。 */
    equals(other: Buffer): boolean;

    /** 查找第一次出现位置，未找到返回 -1。 */
    indexOf(value: number, from?: number): number;

    /**
     * 解码为字符串。encoding 同 from() 一致；缺省 utf8。
     */
    toString(encoding?: string): string;

    /** 拷贝出一个新的 ArrayBuffer。 */
    toArrayBuffer(): ArrayBuffer;

    /** toArrayBuffer 的别名。 */
    toUint8Array(): ArrayBuffer;

    // 数值读写（little/big endian）
    writeUInt8(value: number, offset?: number): void;

    writeInt8(value: number, offset?: number): void;

    writeUInt16LE(value: number, offset?: number): void;

    writeUInt16BE(value: number, offset?: number): void;

    writeInt32LE(value: number, offset?: number): void;

    writeInt32BE(value: number, offset?: number): void;

    writeFloat32LE(value: number, offset?: number): void;

    writeFloat64LE(value: number, offset?: number): void;

    readUInt8(offset?: number): number;

    readInt8(offset?: number): number;

    readUInt16LE(offset?: number): number;

    readUInt16BE(offset?: number): number;

    readInt32LE(offset?: number): number;

    readInt32BE(offset?: number): number;

    readFloat32LE(offset?: number): number;

    readFloat64LE(offset?: number): number;
}

/**
 * 统一字节流抽象。包装一个底层 System.IO.Stream（文件、HTTP 响应、内存等），
 * 提供异步 read/write 与 C# 端实现的 pipe（避免 per-chunk 的 JS↔native 往返）。
 *
 * **生命周期**：所有 Stream 由引擎登记跟踪——即便 JS 忘记 close()，
 * 引擎销毁时也会强制释放底层句柄。建议显式 close。
 */
declare class Stream {
    private constructor();

    readonly readable: boolean;
    readonly writable: boolean;
    readonly closed: boolean;
    /** 当前位置；不可 seek 时为 -1。 */
    readonly position: number;
    /** 总长度；不可 seek 时为 -1。 */
    readonly length: number;

    /** 创建可读可写的内存 Stream（基于 MemoryStream）。 */
    static memory(initialCapacity?: number): Stream;

    /**
     * 从底层流读取最多 length 字节到 buf 的 [offset, offset+length) 区间。
     * 返回实际读入字节数；0 表示 EOF。length 缺省 / <=0 时使用 buf.length-offset。
     */
    read(buf: Buffer, offset?: number, length?: number): Promise<number>;

    /**
     * 写入数据，data 可为 Buffer / ArrayBuffer / Uint8Array / 字符串（UTF-8）。
     * 返回写入字节数。length 缺省 / <=0 时写入剩余全部。
     */
    write(data: Buffer | ArrayBuffer | ArrayBufferView | string, offset?: number, length?: number): Promise<number>;

    /** 刷新缓冲区到底层句柄。 */
    flush(): Promise<void>;

    /** 关闭。幂等；关闭后其它方法抛异常。 */
    close(): Promise<void>;

    /**
     * 将本流剩余字节按 chunkSize（缺省/<=0 时 64 KiB）拷贝到 dest，
     * 完成后**始终**关闭 dest。返回已拷贝字节数。
     */
    pipe(dest: Stream, chunkSize?: number): Promise<number>;

    /**
     * 一次读取剩余全部字节到新 Buffer。maxBytes 缺省/<=0 时不限。
     */
    readAll(maxBytes?: number): Promise<Buffer>;
}

/** DirectoryStream.read() 返回的目录项。 */
interface DirEntry {
    name: string;
    isFile: boolean;
    isDirectory: boolean;
    /** 对目录始终为 0。 */
    size: number;
}

/**
 * 按批枚举目录的流式入口。比一次性 readDir 更适合超大目录或边读边处理。
 * 与 Stream 一样受引擎生命周期跟踪。
 */
declare class DirectoryStream {
    private constructor();

    readonly closed: boolean;

    /**
     * 取下一批最多 maxEntries 个条目；返回 null 表示已枚举完毕。
     * maxEntries 缺省 / <=0 时按内部默认（64）批量返回。
     */
    read(maxEntries?: number): Promise<DirEntry[] | null>;

    close(): Promise<void>;
}

// ═══════════════════════════════════════════════════════════════════════════
// 同步 fs 模块
// ═══════════════════════════════════════════════════════════════════════════

/**
 * 同步文件系统模块。除流式入口（openRead/openWrite/openDir）以外，
 * 所有方法在调用线程上阻塞执行。
 */
declare const fs: {
    readFile(path: string, encoding?: string): string;
    readFileBytes(path: string): ArrayBuffer;
    writeFile(path: string, content: string, encoding?: string): boolean;
    writeFileBytes(path: string, buffer: Buffer | ArrayBufferView | ArrayBuffer): boolean;
    appendFile(path: string, content: string, encoding?: string): boolean;
    exists(path: string): boolean;
    isFile(path: string): boolean;
    isDirectory(path: string): boolean;
    remove(path: string): boolean;
    rename(oldPath: string, newPath: string): boolean;
    copy(src: string, dst: string): boolean;
    mkdir(path: string): boolean;
    readDir(path: string): string[];
    stat(path: string): {
        size: number;
        isFile: boolean;
        isDirectory: boolean;
        created: string;
        modified: string;
        accessed: string;
    } | null;
    getcwd(): string;
    tempDir(): string;

    /** 打开文件用于流式读取。 */
    openRead(path: string): Stream;
    /** 打开文件用于写入；append=true 时追加到文件末尾。 */
    openWrite(path: string, append?: boolean): Stream;
    /** 打开目录用于按批枚举。 */
    openDir(path: string): DirectoryStream;
};


// ─────────────────────────────────────────────────────────────────────────────
// ES Modules (import / export)
// ─────────────────────────────────────────────────────────────────────────────
//
// QuickJsNet 支持标准 ES Module 语法：`import` / `export` / 顶层 `await` /
// 动态 `import()`。模块来源由宿主 (.NET) 通过 `engine.Modules` 配置：
//
//   1. 虚拟模块  : engine.Modules.Register("math", "export const add=(a,b)=>a+b;")
//   2. 文件系统 : engine.Modules.BasePath = "/path/to/scripts" 后用 "./a.js"
//   3. 自定义解析: engine.Modules.Resolver = name => ...
//   4. 原生模块  : engine.Modules.RegisterNative("app:foo", b => { b.Export("x", 1); b.ExportFunc("hi", () => "hi"); })
//
// JS 端无需声明；下列示例展示用法：
//
//   import { add } from "math";
//   import defaultThing from "./other.js";
//   const m = await import("math");
//   console.log(import.meta.url);    // file:///... 或 qjs-virtual:name 等
//
// 内置模块（fs / fsAsync / fetch / Buffer / Stream / 定时器等）默认仅以
// 全局形式提供。打开 `QuickJSEngineOptions.BuiltinAsModule = true` 后可：
//
//   import { readFileSync } from "qjs:fs";
//   import { fetch } from "qjs:fetch";
//   import { Buffer } from "qjs:buffer";
//   import { setTimeout } from "qjs:timers";
//
// 进一步 `BuiltinAsModuleOnly = true` 同时移除 globals（沙箱场景）。
//
// import.meta:
//   - import.meta.url   : 模块 URL (file:/// | qjs-virtual: | qjs-native: | qjs-resolver:)
//   - import.meta.main  : boolean，目前固定 false（保留字段）
//
// 限制：
//  - 不支持 `import attributes` / `import assert`
//  - 不支持 HTTP(S) 远端模块加载
//  （模块源码大小限制已解除）
