Public Class PageSetupSystem


    Private Sub PageSetupSystem_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        If BuildType = BuildTypes.Release Then
            PanDonate.Visibility = Visibility.Collapsed
        Else
            PanDonate.Visibility = Visibility.Visible
            ItemSystemUpdateDownload.Content = "在有新版本时自动下载（更新快照版前需要先输入土豆码）"
        End If

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        Reload()
        SliderLoad()
        AniControlEnabled -= 1

    End Sub
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化启动器页设置")
            Hint("已初始化启动器页设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    '滑动条
    Private Sub SliderLoad()
        SliderDownloadThread.GetHintText = Function(v) v + 1
        SliderDownloadSpeed.GetHintText =
        Function(v)
            Select Case v
                Case Is <= 14
                    Return (v + 1) * 0.1 & " M/s"
                Case Is <= 31
                    Return (v - 11) * 0.5 & " M/s"
                Case Is <= 41
                    Return (v - 21) & " M/s"
                Case Else
                    Return "无限制"
            End Select
        End Function
        SliderDebugAnim.GetHintText = Function(v) If(v > 29, "关闭", (v / 10 + 0.1) & "x")
    End Sub
    Private Sub SliderDownloadThread_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderDownloadThread.PreviewChange
        If SliderDownloadThread.Value < 100 Then Return
        If Not Settings.Get(Of Boolean)("HintDownloadThread") Then
            Settings.Set("HintDownloadThread", True)
            MyMsgBox("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。" & vbCrLf &
                     "一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！", "警告", "我知道了", IsWarn:=True)
        End If
    End Sub

    '识别码/土豆码替代入口
    Private Sub BtnSystemIdentify_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemIdentify.Click
        PageOtherAbout.CopyIdentify()
    End Sub
    Private Sub BtnSystemUnlock_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemUnlock.Click
        InputPotatoCode(False)
    End Sub

    '调试模式
    Private Sub CheckDebugMode_Change() Handles CheckDebugMode.Change
        If AniControlEnabled = 0 Then Hint("部分调试信息将在刷新或启动器重启后切换显示！",, False)
    End Sub

    '自动更新
    Private Sub ComboSystemActivity_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemActivity.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemActivity.SelectedIndex <> 2 Then Return
        If MyMsgBox("若选择此项，即使在将来出现严重问题时，你也无法获取相关通知。" & vbCrLf &
                    "例如，如果发现某个版本游戏存在严重 Bug，你可能就会因为无法得到通知而导致无法预知的后果。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重要通知时显示公告 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemActivity.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub ComboSystemUpdate_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ComboSystemUpdate.SelectionChanged
        If AniControlEnabled <> 0 Then Return
        If ComboSystemUpdate.SelectedIndex <> 3 Then Return
        If MyMsgBox("若选择此项，即使在启动器将来出现严重问题时，你也无法获取更新并获得修复。" & vbCrLf &
                    "例如，如果官方修改了登录方式，从而导致现有启动器无法登录，你可能就会因为无法更新而无法开始游戏。" & vbCrLf & vbCrLf &
                    "一般选择 仅在有重大漏洞更新时显示提示 就可以让你尽量不受打扰了。" & vbCrLf &
                    "除非你在制作服务器整合包，或时常手动更新启动器，否则极度不推荐选择此项！", "警告", "我知道我在做什么", "取消", IsWarn:=True) = 2 Then
            If e.RemovedItems.Count > 0 Then ComboSystemUpdate.SelectedItem = e.RemovedItems(0)
        End If
    End Sub
    Private Sub BtnSystemUpdate_Click(sender As Object, e As EventArgs) Handles BtnSystemUpdate.Click
        UpdateCheckByButton()
    End Sub
    ''' <summary>
    ''' 启动器是否已经是最新版？
    ''' 若返回 Nothing，则代表无更新缓存文件或出错。
    ''' </summary>
    Public Shared Function IsLauncherNewest() As Boolean?
        Try
            '确认服务器公告是否正常
            Dim ServerContent As String = If(FileUtils.TryReadAsString(PathTemp & "Cache\Notice.cfg"), "")
            If ServerContent.Split("|").Count < 3 Then Return Nothing
            '确认是否为最新
            Return ServerContent.Split("|")(If(BuildType = BuildTypes.Release, 2, 1)) <= VersionCode
        Catch ex As Exception
            Logger.Error(ex, "确认启动器更新失败")
            Return Nothing
        End Try
    End Function

#Region "导出 / 导入设置"

    Private Sub BtnSystemSettingExp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingExp.Click
        Hint("该功能尚在开发中！")
    End Sub
    Private Async Sub BtnSystemSettingImp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingImp.Click
        Await ImportDetectedPcl2Async()
    End Sub

#End Region

#Region "PCL2 数据导入"

    Private Pcl2ImportCancellation As CancellationTokenSource
    Private Pcl2ImportExitRequested As Boolean
    Private Pcl2ImportExitWarning As Boolean

    ''' <summary>退出时先等待导入器取消和回滚，避免在复制或写入设置时终止进程。</summary>
    Public Function FinishPcl2ImportBeforeExit(SendWarning As Boolean) As Boolean
        If Pcl2ImportCancellation Is Nothing Then Return False
        Pcl2ImportExitRequested = True
        Pcl2ImportExitWarning = SendWarning
        Pcl2ImportCancellation.Cancel()
        LabPcl2ImportStatus.Text = "正在结束导入，完成后退出启动器……"
        Return True
    End Function

    Public Async Function ImportDetectedPcl2Async(Optional Startup As Boolean = False) As Task
        If Startup AndAlso LoadPcl2ImportState() Then Return
        Await RunPcl2ImportAsync(Nothing, Startup)
    End Function

    Private Async Sub BtnPcl2Import_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPcl2Import.Click
        Await ImportDetectedPcl2Async()
    End Sub

    Private Async Sub BtnPcl2ImportBrowse_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPcl2ImportBrowse.Click
        If Pcl2ImportCancellation IsNot Nothing Then Return
        Dim Folder As String = Dialogs.SelectFolder("选择原 PCL2 启动器所在文件夹或 PCL 数据文件夹", False).FirstOrDefault()
        If Not String.IsNullOrEmpty(Folder) Then Await RunPcl2ImportAsync(Folder, False)
    End Sub

    Private Function LoadPcl2ImportState() As Boolean
        Try
            If Not File.Exists(Pcl2ImportStateFile) Then Return False
            IniClearCache(Pcl2ImportStateFile)
            If ReadIni(Pcl2ImportStateFile, "Completed") <> "1" Then Return False
            LabPcl2ImportStatus.Text = Encoding.UTF8.GetString(Convert.FromBase64String(ReadIni(Pcl2ImportStateFile, "Status")))
            LabPcl2ImportStatus.Visibility = Visibility.Visible
            Dim Backup = ReadIni(Pcl2ImportStateFile, "Backup")
            BtnPcl2ImportBackup.Tag = Backup
            BtnPcl2ImportBackup.Visibility = If(Directory.Exists(Backup), Visibility.Visible, Visibility.Collapsed)
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub SavePcl2ImportState(Status As String, Backup As String)
        Dim Temporary = Pcl2ImportStateFile & ".tmp"
        Try
            File.WriteAllLines(Temporary, {"Completed:1", "Status:" & Convert.ToBase64String(Encoding.UTF8.GetBytes(Status)), "Backup:" & Backup}, New UTF8Encoding(False))
            If File.Exists(Pcl2ImportStateFile) Then
                File.Replace(Temporary, Pcl2ImportStateFile, Nothing)
            Else
                File.Move(Temporary, Pcl2ImportStateFile)
            End If
            IniClearCache(Pcl2ImportStateFile)
        Catch ex As Exception
            Logger.Warn(ex, "保存 PCL2 自动导入状态失败，下次启动将重新检查")
        Finally
            Try
                If File.Exists(Temporary) Then File.Delete(Temporary)
            Catch ex As Exception
                Logger.Warn(ex, "清理 PCL2 自动导入状态临时文件失败")
            End Try
        End Try
    End Sub

    Private Async Function RunPcl2ImportAsync(Folder As String, Startup As Boolean) As Task
        If Pcl2ImportCancellation IsNot Nothing Then Return
        Dim Options As New Pcl2ImportOptions With {
            .Settings = Startup OrElse CheckPcl2Settings.Checked,
            .Appearance = Startup OrElse CheckPcl2Appearance.Checked,
            .GameFolders = Startup OrElse CheckPcl2Folders.Checked,
            .Overwrite = Not Startup AndAlso CheckPcl2Overwrite.Checked
        }
        If Not (Options.Settings OrElse Options.Appearance OrElse Options.GameFolders) Then
            Hint("请至少选择一类要导入的数据。", HintType.Red)
            Return
        End If
        Dim Destination = Paths.Base & "PCL\"
        Dim Definitions = GetPcl2ImportDefinitions()
        Dim Registry As New OpenLaunchImportRegistry
        Dim Cancellation As New CancellationTokenSource
        Pcl2ImportCancellation = Cancellation
        BtnPcl2Import.IsEnabled = False
        BtnPcl2ImportBrowse.IsEnabled = False
        PanPcl2ImportOptions.IsEnabled = False
        BtnPcl2ImportCancel.IsEnabled = True
        BtnPcl2ImportCancel.Visibility = Visibility.Visible
        ProgressPcl2Import.Visibility = Visibility.Visible
        LabPcl2ImportStatus.Visibility = Visibility.Visible
        LabPcl2ImportStatus.Text = "正在自动寻找 PCL2 数据……"
        Dim Applied As Boolean = False
        Try
            Dim SourceRegistry = Await Task.Run(Function() ReadPcl2Preferences())
            Dim RegistryOnly As Boolean = False
            Dim SourceLabel As String = Folder
            If String.IsNullOrEmpty(Folder) Then
                Dim Detection = Await Task.Run(Function() Pcl2Detector.Detect(GetPcl2SearchRoots(), Destination, SourceRegistry, Cancellation.Token))
                Cancellation.Token.ThrowIfCancellationRequested()
                If Not Detection.Found Then
                    LabPcl2ImportStatus.Text = "暂未发现 PCL2 数据。找到旧启动器后可再次自动检测，也可手动选择其所在文件夹。"
                    Return
                End If
                RegistryOnly = Detection.RegistryOnly
                Folder = If(Detection.SourceFolder, Paths.Base)
                SourceLabel = If(RegistryOnly, "本机 PCL2 设置与游戏目录", Detection.SourceFolder)
            End If
            LabPcl2ImportStatus.Text = "已找到：" & SourceLabel & vbCrLf & "正在准备导入……"
            '等待启动阶段的目录扫描结束，避免其旧结果覆盖刚导入的目录列表。
            Await Task.Run(
                Sub()
                    Do While McFolderListLoader.State = LoadState.Loading
                        Cancellation.Token.ThrowIfCancellationRequested()
                        Thread.Sleep(20)
                    Loop
                End Sub)
            Dim Plan = Await Task.Run(
                Function() Pcl2Importer.Scan(Folder, Destination, SourceRegistry,
                    Definitions, Registry, Options, Cancellation.Token, RegistryOnly))
            Cancellation.Token.ThrowIfCancellationRequested()
            If Not Plan.HasChanges Then
                LabPcl2ImportStatus.Text = "已检测 PCL2 数据，OpenLaunch 已有内容已保留，无需重复导入。" & vbCrLf & "来源：" & SourceLabel
                SavePcl2ImportState(LabPcl2ImportStatus.Text, If(TryCast(BtnPcl2ImportBackup.Tag, String), ""))
                Return
            End If
            If Options.Overwrite Then
                Dim Preview = $"来源：{SourceLabel}{vbCrLf}{vbCrLf}" &
                    $"设置：{Plan.SettingCount} 项；个性化文件：{Plan.AppearanceFileCount} 个；新游戏目录：{Plan.GameFolderCount} 个。{vbCrLf}{vbCrLf}" &
                    "将覆盖同名设置与个性化文件，替换前自动备份。原游戏目录中的存档和 Mod 将由两个启动器共用。"
                If MyMsgBox(Preview, "确认覆盖导入", "开始导入", "取消") <> 1 Then
                    LabPcl2ImportStatus.Text = "已取消导入。"
                    Return
                End If
            End If
            Dim Progress As IProgress(Of String) = New System.Progress(Of String)(
                Sub(Name) LabPcl2ImportStatus.Text = "正在导入：" & Name)
            Dim Result = Await Task.Run(Function() Pcl2Importer.Import(Plan, Registry, Cancellation.Token, Progress))
            Applied = True
            IniClearCache("Setup")
            For Each Key In Plan.ImportedSettingKeys
                If Settings.Entries.ContainsKey(Key) Then Settings.Entries(Key).ValueCache.Clear()
            Next
            AniControlEnabled += 1
            Try
                Reload()
                For Each Key In Plan.ImportedSettingKeys
                    If Settings.Entries.ContainsKey(Key) Then
                        Dim Entry = Settings.Entries(Key)
                        If Entry.OnChanged IsNot Nothing Then Entry.OnChanged.Invoke(Settings.Get(Key))
                    End If
                Next
                FrmMain.UpdateBackgroundAndTitleBar()
                PageSetupUI.BackgroundRefresh(False, False)
                MusicRefreshPlay(False)
                If FrmMain.PageCurrent.Page = FormMain.PageType.Launch Then FrmLaunchRight.ForceRefresh()
            Finally
                AniControlEnabled -= 1
            End Try
            If Plan.GameFolderCount > 0 Then McFolderListLoader.Start(IsForceRestart:=True)
            LabPcl2ImportStatus.Text = $"已导入 {Plan.SettingCount} 项设置、{Result.CopiedFiles} 个个性化文件、{Plan.GameFolderCount} 个游戏目录（{Plan.InstanceCount} 个版本）。" & vbCrLf &
                "来源：" & SourceLabel & vbCrLf & "游戏目录已可使用，登录账号请重新登录。"
            BtnPcl2ImportBackup.Tag = Result.BackupFolder
            BtnPcl2ImportBackup.Visibility = Visibility.Visible
            SavePcl2ImportState(LabPcl2ImportStatus.Text, Result.BackupFolder)
            If Not Pcl2ImportExitRequested Then Hint("PCL2 数据已自动导入，可在版本列表中选择原有游戏。", HintType.Green)
        Catch ex As OperationCanceledException
            LabPcl2ImportStatus.Text = "已取消导入，本次修改已还原。可随时重新检测。"
            If Startup AndAlso Not Pcl2ImportExitRequested Then SavePcl2ImportState(LabPcl2ImportStatus.Text, "")
        Catch ex As Exception
            Pcl2ImportExitRequested = False
            LabPcl2ImportStatus.Text = If(Applied, "数据已导入，部分界面需要重启后生效。", "自动导入未完成，可稍后重试或手动选择 PCL2 文件夹。")
            If Startup AndAlso Not TypeOf ex Is AggregateException Then
                Logger.Warn(ex, "PCL2 自动导入未完成")
            Else
                Logger.Error(ex, If(Applied, "PCL2 数据已导入，请重启启动器", "PCL2 导入失败，请查看错误详情及备份"), LogBehavior.Alert)
            End If
        Finally
            Pcl2ImportCancellation = Nothing
            Cancellation.Dispose()
            BtnPcl2Import.IsEnabled = True
            BtnPcl2ImportBrowse.IsEnabled = True
            PanPcl2ImportOptions.IsEnabled = True
            BtnPcl2ImportCancel.Visibility = Visibility.Collapsed
            ProgressPcl2Import.Visibility = Visibility.Collapsed
            If Pcl2ImportExitRequested Then
                Pcl2ImportExitRequested = False
                FrmMain.EndProgram(Pcl2ImportExitWarning)
            End If
        End Try
    End Function

    Private Sub BtnPcl2ImportBackup_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPcl2ImportBackup.Click
        Dim Folder = TryCast(BtnPcl2ImportBackup.Tag, String)
        If Not String.IsNullOrEmpty(Folder) AndAlso Directory.Exists(Folder) Then OpenExplorer(Folder)
    End Sub

    Private Sub BtnPcl2ImportCancel_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnPcl2ImportCancel.Click
        Pcl2ImportCancellation?.Cancel()
        BtnPcl2ImportCancel.IsEnabled = False
        LabPcl2ImportStatus.Text = "正在取消并还原本次修改……"
    End Sub

#End Region

End Class
