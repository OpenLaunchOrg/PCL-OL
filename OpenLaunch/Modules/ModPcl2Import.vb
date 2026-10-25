Imports Microsoft.Win32

Public Module ModPcl2Import

    Public ReadOnly Property Pcl2ImportStateFile As String
        Get
            Return Paths.Base & "PCL\Pcl2ImportState.ini"
        End Get
    End Property

    ''' <summary>优先检测正在运行的启动器、快捷方式和常见的便携版存放位置。</summary>
    Public Function GetPcl2SearchRoots() As String()
        Dim Roots As New List(Of String)
        For Each Name In {"PCL", "PCL2", "Plain Craft Launcher", "Plain Craft Launcher 2"}
            For Each Running In Process.GetProcessesByName(Name)
                Try
                    Dim FileName = Running.MainModule?.FileName
                    If Not String.IsNullOrEmpty(FileName) Then Roots.Add(Path.GetDirectoryName(FileName))
                Catch ex As Exception
                    '其他进程的模块信息可能不可读，继续使用其余检测方式。
                Finally
                    Running.Dispose()
                End Try
            Next
        Next
        Dim HomeFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        Dim Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        Roots.Add(Desktop)
        Roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
        Roots.Add(Path.Combine(HomeFolder, "Downloads"))
        Roots.Add(Path.Combine(HomeFolder, "Games"))
        Roots.Add(Path.Combine(HomeFolder, "PCL"))
        Roots.Add(Path.Combine(HomeFolder, "PCL2"))
        Roots.Add(Path.Combine(Paths.AppData, "PCL"))
        Dim Parent = Directory.GetParent(PathUtils.RemoveSlashSuffix(Paths.Base))
        If Parent IsNot Nothing Then
            Roots.Add(Parent.FullName)
            For Each Name In {"PCL", "PCL2", "Plain Craft Launcher 2", "Minecraft"}
                Roots.Add(Path.Combine(Parent.FullName, Name))
            Next
        End If
        '只解析已有快捷方式的目标路径，不运行启动器、不保存快捷方式。
        Dim Shell As Object = Nothing
        Try
            Shell = Microsoft.VisualBasic.Interaction.CreateObject("WScript.Shell")
            For Each Folder In {Desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                                Environment.GetFolderPath(Environment.SpecialFolder.Programs)}
                If Not Directory.Exists(Folder) Then Continue For
                For Each LinkFile In Directory.EnumerateFiles(Folder, "*.lnk")
                    Dim Link As Object = Nothing
                    Try
                        Link = Shell.CreateShortcut(LinkFile)
                        Dim Target As String = Link.TargetPath
                        Dim Description = Path.GetFileNameWithoutExtension(LinkFile) & " " & Path.GetFileName(Target)
                        If Description.IndexOf("PCL", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                           Description.IndexOf("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase) >= 0 Then
                            If File.Exists(Target) Then Roots.Add(Path.GetDirectoryName(Target))
                        End If
                    Catch ex As Exception
                        '失效快捷方式不影响自动导入。
                    Finally
                        If Link IsNot Nothing Then Runtime.InteropServices.Marshal.FinalReleaseComObject(Link)
                    End Try
                Next
            Next
        Catch ex As Exception
            '系统未提供脚本宿主或快捷方式目录不可读时，保留其他检测结果。
        Finally
            If Shell IsNot Nothing Then Runtime.InteropServices.Marshal.FinalReleaseComObject(Shell)
        End Try
        Return Roots.Where(Function(Folder) Not String.IsNullOrWhiteSpace(Folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
    End Function

    ''' <summary>只读取当前 Windows 用户的上游偏好设置，不读取账号或登录令牌。</summary>
    Public Function ReadPcl2Preferences() As Dictionary(Of String, String)
        Dim Result As New Dictionary(Of String, String)(StringComparer.Ordinal)
        For Each Branch In {"PCL", "PCL2"}
            Using Source = Registry.CurrentUser.OpenSubKey("Software\" & Branch, False)
                If Source Is Nothing Then Continue For
                For Each Name In Source.GetValueNames()
                    If Not Pcl2Importer.IsPreferenceKey(Name) Then Continue For
                    Dim Value = Source.GetValue(Name, Nothing, RegistryValueOptions.DoNotExpandEnvironmentNames)
                    If Value IsNot Nothing Then Result(Name) = Convert.ToString(Value, Globalization.CultureInfo.InvariantCulture)
                Next
                'PCL 与 PCL2 是不同版本可能采用的注册表分支，不混合多个来源。
                If Result.Count > 0 Then Exit For
            End Using
        Next
        Return Result
    End Function

    Public Function GetPcl2ImportDefinitions() As Pcl2ImportSetting()
        Return Settings.Entries.Values.
            Where(Function(Entry) Not Entry.Encrypted AndAlso Entry.Source <> Settings.Sources.Instance AndAlso Pcl2Importer.IsPreferenceKey(Entry.Key)).
            Select(Function(Entry) New Pcl2ImportSetting(Entry.Key, Entry.Type, Entry.Source = Settings.Sources.Registry)).ToArray()
    End Function

    ''' <summary>直接写入并传播错误，确保导入器可以准确回滚。</summary>
    Public Class OpenLaunchImportRegistry
        Implements IPcl2ImportRegistry

        Public Function Read(Key As String) As String Implements IPcl2ImportRegistry.Read
            Using Target = Registry.CurrentUser.OpenSubKey("Software\" & RegFolder, False)
                Dim Value = Target?.GetValue(Key, Nothing, RegistryValueOptions.DoNotExpandEnvironmentNames)
                Return If(Value Is Nothing, Nothing, Convert.ToString(Value, Globalization.CultureInfo.InvariantCulture))
            End Using
        End Function

        Public Sub Write(Key As String, Value As String) Implements IPcl2ImportRegistry.Write
            Using Target = Registry.CurrentUser.CreateSubKey("Software\" & RegFolder, True)
                If Value Is Nothing Then
                    Target.DeleteValue(Key, False)
                Else
                    Target.SetValue(Key, Value, RegistryValueKind.String)
                End If
            End Using
        End Sub
    End Class

End Module
