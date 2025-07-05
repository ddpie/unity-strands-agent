# Unity AI助手 - MCP (Model Context Protocol) 集成

## 概述

Unity AI助手现在支持MCP (Model Context Protocol)，允许AI助手连接到外部工具和服务，大大扩展其功能范围。MCP是一个开放协议，用于标准化应用程序如何向大型语言模型提供上下文。

## 功能特性

### 支持的MCP传输协议
1. **Stdio传输** - 通过标准输入输出与本地工具通信
2. **HTTP传输** - 通过HTTP协议与远程服务通信
3. **SSE传输** - 通过Server-Sent Events实现实时通信

### 核心功能
- **多服务器支持** - 同时连接多个MCP服务器
- **自动工具发现** - 自动发现并集成MCP服务器提供的工具
- **配置管理** - 通过Unity Inspector界面管理MCP配置
- **环境变量支持** - 安全管理API密钥和敏感信息
- **错误处理** - 完善的错误处理和重试机制
- **日志记录** - 详细的操作日志和调试信息

## 安装和设置

### 1. 运行设置向导
```
Window -> AI助手 -> 设置向导
```

设置向导包含两个标签页：
- **设置进度**：显示AI助手的安装和配置进度
- **MCP配置**：配置MCP服务器和JSON设置

设置向导会自动安装所需的MCP依赖包：
- `mcp>=1.0.0`
- `strands-mcp>=0.1.0`

### 2. 配置MCP服务器

在设置向导的"MCP配置"标签页中，您可以：
- 启用/禁用MCP功能
- 添加预设的MCP服务器配置
- 直接编辑JSON配置
- 验证和导出配置

## 预设MCP服务器

### AWS文档服务器
```json
{
    "name": "AWS文档",
    "description": "AWS官方文档搜索和查询",
    "transport_type": "stdio",
    "command": "uvx",
    "args": ["awslabs.aws-documentation-mcp-server@latest"]
}
```

### GitHub服务器
```json
{
    "name": "GitHub",
    "description": "GitHub仓库管理和搜索",
    "transport_type": "stdio",
    "command": "uvx",
    "args": ["mcp-server-github"],
    "env_vars": {
        "GITHUB_TOKEN": "your_github_token"
    }
}
```

### 文件系统服务器
```json
{
    "name": "文件系统",
    "description": "本地文件系统访问",
    "transport_type": "stdio",
    "command": "uvx",
    "args": ["mcp-server-filesystem", "--base-path", "/path/to/unity/project"]
}
```

### Web搜索服务器
```json
{
    "name": "Web搜索",
    "description": "网络搜索和信息检索",
    "transport_type": "http",
    "url": "http://localhost:8000/mcp"
}
```

## 配置说明

### 基本配置
- **启用MCP**: 控制是否启用MCP功能
- **最大并发连接数**: 同时连接的MCP服务器数量限制
- **默认超时时间**: MCP操作的默认超时时间

### 服务器配置
每个MCP服务器需要以下配置：

#### 基本信息
- **名称**: 服务器的显示名称
- **描述**: 服务器功能描述
- **启用状态**: 是否启用此服务器

#### 传输配置
根据传输类型选择不同的配置选项：

**Stdio传输**:
- **命令**: 启动MCP服务器的命令
- **参数**: 命令行参数数组
- **工作目录**: 服务器的工作目录

**HTTP传输**:
- **URL**: MCP服务器的HTTP端点
- **超时时间**: HTTP请求超时时间

#### 环境变量
- **键**: 环境变量名称
- **值**: 环境变量值
- **敏感信息**: 标记是否为敏感信息（如API密钥）

#### 高级选项
- **自动重启**: 服务器异常时是否自动重启
- **最大重试次数**: 连接失败时的最大重试次数
- **记录输出日志**: 是否记录服务器输出日志

## 使用指南

### 1. 基础设置
1. 打开设置向导：`Window -> AI助手 -> 设置向导`
2. 在"设置进度"标签页完成AI助手安装
3. 切换到"MCP配置"标签页
4. 启用MCP功能
5. 配置所需的MCP服务器

### 2. 添加预设服务器
1. 在"MCP配置"标签页中，展开"预设配置"
2. 点击相应的按钮添加预设服务器：
   - **AWS文档**：用于查询AWS官方文档
   - **GitHub**：用于GitHub仓库管理（需要GITHUB_TOKEN）
   - **文件系统**：用于本地文件访问
   - **Web搜索**：用于网络搜索
3. 服务器会被添加到配置中（默认禁用状态）

### 3. JSON配置编辑
1. 展开"JSON配置编辑"区域
2. 直接编辑JSON配置文本
3. 点击"应用JSON配置"生效
4. 使用"更新JSON"从当前配置生成JSON

### 4. 配置管理
- **验证配置**：检查当前配置是否有效
- **导出JSON文件**：将配置导出为.json文件
- **重置配置**：重置为默认设置

### 5. 验证和测试
1. 使用"验证配置"按钮检查配置
2. 查看Unity Console确认无错误信息
3. 在AI助手中测试MCP工具功能

## 环境变量管理

### 敏感信息处理
对于API密钥等敏感信息，建议：
1. 标记为"敏感信息"
2. 在系统环境变量中设置实际值
3. 配置文件中只保存环境变量名称

### 示例环境变量设置
```bash
# GitHub Token
export GITHUB_TOKEN="ghp_your_github_token_here"

# AWS Credentials
export AWS_ACCESS_KEY_ID="your_access_key"
export AWS_SECRET_ACCESS_KEY="your_secret_key"
export AWS_DEFAULT_REGION="us-east-1"
```

## 故障排除

### 常见问题

#### MCP包安装失败
```
解决方案：
1. 检查Python虚拟环境是否正确创建
2. 确保网络连接正常
3. 尝试手动安装：pip install mcp>=1.0.0
```

#### MCP服务器连接失败
```
解决方案：
1. 检查命令和参数是否正确
2. 确认所需的外部工具已安装（如uvx）
3. 检查环境变量是否设置正确
4. 查看Unity Console的错误日志
```

#### 工具未出现在AI助手中
```
解决方案：
1. 确认MCP已启用
2. 检查服务器是否启用
3. 验证服务器配置是否正确
4. 重启Unity AI助手
```

### 调试技巧
1. 启用"记录输出日志"查看详细信息
2. 使用Unity Console查看MCP相关日志
3. 在终端中手动测试MCP服务器
4. 使用"验证配置"功能检查配置问题

## 开发和扩展

### 创建自定义MCP服务器
如果需要创建自定义MCP服务器，请参考：
- [MCP官方文档](https://modelcontextprotocol.io/)
- [Python MCP SDK](https://github.com/modelcontextprotocol/python-sdk)
- [MCP服务器示例](https://github.com/modelcontextprotocol/servers)

### 集成第三方MCP服务器
1. 确认服务器支持MCP协议
2. 获取启动命令和参数
3. 在Unity中添加服务器配置
4. 测试连接和工具功能

## 安全注意事项

1. **敏感信息保护**: 
   - 不要在配置文件中存储明文密钥
   - 使用环境变量管理敏感信息
   - 定期轮换API密钥

2. **网络安全**:
   - 仅连接可信的MCP服务器
   - 使用HTTPS进行远程连接
   - 设置适当的超时时间

3. **权限控制**:
   - 限制MCP服务器的文件访问权限
   - 仅授予必要的API权限
   - 定期审查服务器配置

## 更新日志

### v1.0.0
- 初始MCP支持
- Stdio和HTTP传输协议
- 预设服务器配置
- Unity Inspector集成
- 环境变量管理
- 错误处理和日志记录

## 贡献

欢迎贡献代码、报告问题或提出改进建议。请在GitHub上提交Issue或Pull Request。

## 许可证

本项目遵循MIT许可证。详细信息请查看LICENSE文件。