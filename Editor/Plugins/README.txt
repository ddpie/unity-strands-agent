Python.NET DLL 安装说明
========================

请将 Python.Runtime.dll 放置在此目录中。

获取方式：

1. 通过 NuGet 下载：
   - 访问: https://www.nuget.org/packages/pythonnet/
   - 下载对应版本的包
   - 解压后找到 Python.Runtime.dll

2. 或者通过命令行：
   ```bash
   # 创建临时目录
   mkdir temp && cd temp
   
   # 下载 NuGet 包
   curl -o pythonnet.nupkg https://www.nuget.org/api/v2/package/pythonnet/3.0.3
   
   # 解压
   unzip pythonnet.nupkg
   
   # DLL 位置: lib/netstandard2.0/Python.Runtime.dll
   ```

3. 文件结构应该是：
   Editor/
   └── Plugins/
       ├── Python.Runtime.dll
       └── README.txt (本文件)

注意：
- 推荐使用 Python.NET 3.0.3 版本
- 确保 DLL 与您的 Unity 版本兼容
- 重启 Unity Editor 使更改生效