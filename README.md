# PCL-OL

[源码仓库](https://github.com/OpenLaunchOrg/PCL-OL) · [CI 构建与下载](https://github.com/OpenLaunchOrg/PCL-OL/actions) · [版本发布](https://github.com/OpenLaunchOrg/PCL-OL/releases)

<img src="OpenLaunch/Images/openlaunch.svg" width="80" height="80" alt="PCL-OL icon" />

PCL-OL 是基于 Plain Craft Launcher（PCL）独立修改的第三方 Minecraft 启动器，包含 WPF 界面、下载、实例管理与游戏启动模块。

PCL-OL 与上游项目独立维护。原项目的作者、贡献者和许可声明保留在代码、关于页面和 [LICENCE](LICENCE) 中。

初始代码基于上游 [2.99.0.1 快照](https://github.com/Meloong-Git/PCL/commit/93ede5d)，本仓库独立记录 PCL-OL 后续开发历史。

## 构建

在 Windows 上安装 Visual Studio 2022，启用 .NET 桌面开发及 .NET Framework 4.8 开发工具后：

```powershell
git submodule update --init --recursive
dotnet restore OpenLaunchCore\OpenLaunchCore.csproj
msbuild OpenLaunch.sln -p:Configuration=Release
```

启动器输出为 `OpenLaunch\bin\OpenLaunch.exe`。GitHub Actions 为 Debug 和 Release 配置生成 PCL-OL 构建产物。

如需自行配置微软登录或 CurseForge API，可设置 `OPENLAUNCH_MS_CLIENT_ID` 和 `OPENLAUNCH_CURSEFORGE_API_KEY` 环境变量；旧的 `PCL_MS_CLIENT_ID` 和 `PCL_CURSEFORGE_API_KEY` 也可继续使用。

## 从 PCL2 自动迁移

首次启动 PCL-OL 时，会在后台自动寻找并导入原 PCL2 的数据，无需选择文件夹或逐项操作。完成后显示简短提示，并立即更新游戏目录和界面。

- 自动检测正在运行的 PCL2、桌面与开始菜单快捷方式、桌面/下载/文档等常见位置，以及 PCL2 注册表中记录的游戏路径附近的启动器数据。
- 若发现多个安装位置，使用设置文件最近更新的一份作为偏好与个性化来源；也会注册当前用户 PCL2 记录的游戏目录。
- 即使找不到原启动器文件夹，也可从当前 Windows 用户的 PCL2 注册表导入可用的偏好设置与游戏目录。
- 默认保留 PCL-OL 已有设置和同名文件，原 PCL2 数据在迁移时不会被修改。
- 游戏目录直接加入版本列表，不复制大型游戏文件。两个启动器将共用原目录中的存档、Mod、资源包和实例设置。
- 迁移主页、背景、音乐与自定义帮助文件；登录令牌、设备身份和赞助数据不迁移，账号需要重新登录。
- 替换前自动在 `PCL/ImportBackups/PCL2-时间-标识/` 保存目标文件及注册表备份；失败或取消时自动还原本次修改。关闭启动器时会先安全结束导入。
- 成功后记录当前安装的迁移状态，不会在每次启动时重复导入。未发现旧数据时会在下次启动继续尝试。

在 **设置 → 其他 → PCL2 数据自动迁移** 中，可以再次自动检测、查看备份或展开高级选项。手动选择文件夹保留为检测不到便携版时的备用入口。覆盖已有内容需要单独勾选并确认，首次启动的自动迁移始终保留已有内容。

检测有目录数量和深度限制，并跳过游戏资源、系统目录与网络根路径，避免扫描整个磁盘。

运行检测与导入引擎的集成测试（.NET 8，无第三方测试依赖）：

```sh
dotnet run --project tests/Pcl2Import.Tests/Pcl2Import.Tests.csproj
```

## CI/CD

构建、测试与打包均由 GitHub 托管运行器执行。每次推送到 `main` 或提交 Pull Request 时，先在 Windows 与 Linux 运行迁移集成测试，再构建 Windows Debug 和 Release 包。运行完成后可在 Actions 页面下载带 SHA-256 校验文件的构建产物。

推送 `v*` 版本标签后，流水线会在全部检查通过后自动发布 GitHub Release，并附上 Windows Release 包。也支持从 Actions 页面手动触发构建。

## 图标

图标采用简洁的开放方框与右上箭头，源文件为 [openlaunch.svg](OpenLaunch/Images/openlaunch.svg)。界面使用由同一图形生成的 WPF 矢量资源，Windows 程序图标和皮肤资源包缩略图也由该 SVG 生成。

修改 SVG 后运行（仅需 Python 3，无第三方依赖）：

```sh
python scripts/generate-icons.py
python scripts/generate-icons.py --check
```

## 数据与上游兼容

为继续读取现有设置、实例、皮肤资源包和整合包，保留 `PCL` 数据目录、`PCL.ini`、旧注册表键、内嵌字体名称及相关格式标识。自定义主页和帮助页面支持原来的 XAML 命名空间与占位符，也支持 PCL-OL 命名。

上游帮助库、服务地址和历史补丁仍保留真实名称与地址，避免将上游服务误标为 PCL-OL 服务。仓库中的 `最新正式版.zip` 是上游历史发布存档，不是 PCL-OL 构建产物。

上游资源：[Plain Craft Launcher](https://github.com/Meloong-Git/PCL) · [帮助文档库](https://github.com/LTCatt/PCL2Help)
