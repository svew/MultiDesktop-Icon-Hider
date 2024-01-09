using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WindowsDesktop;

namespace DesktopIconHiderWPF;

partial class MainWindow
{
    private const int _delay = 2000;
    private IDisposable? _applicationViewChangedListener;

    public ObservableCollection<VirtualDesktopViewModel> Desktops { get; } = new();

    private Guid currentId = Guid.Empty;

    public MainWindow()
    {
        ConfigHelper.Load();
        this.InitializeComponent();
        this.InitializeComObjects();
    }

    private void InitializeComObjects()
    {
        VirtualDesktop.Configure();

        VirtualDesktop.Created += (_, desktop) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                this.Desktops.Add(new VirtualDesktopViewModel(desktop));
                Debug.WriteLine($"Created: {desktop.Name}");
            });
        };

        VirtualDesktop.CurrentChanged += (_, args) =>
        {
            var currentHidden = ConfigHelper.Get(currentId);
            this.currentId = args.NewDesktop.Id;
            var hidden = ConfigHelper.Get(currentId);

            if (currentHidden != hidden)
            {
                SetIconsToggleText(hidden);
                FileHiderHelper.SetHiddenFiles(hidden);
            }

            foreach (var desktop in this.Desktops)
            {
                desktop.IsCurrent = desktop.Id == this.currentId;
            }
            Debug.WriteLine($"Switched to {currentId}");
        };

        VirtualDesktop.Moved += (_, args) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                this.Desktops.Move(args.OldIndex, args.NewIndex);
                Debug.WriteLine($"Moved: {args.OldIndex} -> {args.NewIndex}, {args.Desktop}");
            });
        };

        VirtualDesktop.Destroyed += (_, args) =>
        {
            this.Dispatcher.Invoke(() =>
            {
                var target = this.Desktops.FirstOrDefault(x => x.Id == args.Destroyed.Id);
                if (target != null)
                {
                    this.Desktops.Remove(target);
                    ConfigHelper.Remove(target.Id);
                }
            });
        };

        VirtualDesktop.Renamed += (_, args) =>
        {
            var desktop = this.Desktops.FirstOrDefault(x => x.Id == args.Desktop.Id);
            if (desktop != null) desktop.Name = args.Name;
            Debug.WriteLine($"Renamed: {args.Desktop}");
        };

        VirtualDesktop.WallpaperChanged += (_, args) =>
        {
            var desktop = this.Desktops.FirstOrDefault(x => x.Id == args.Desktop.Id);
            if (desktop != null) desktop.WallpaperPath = new Uri(args.Path);
            Debug.WriteLine($"Wallpaper changed: {args.Desktop}, {args.Path}");
        };

        currentId = VirtualDesktop.Current.Id;
        foreach (var desktop in VirtualDesktop.GetDesktops())
        {
            var vm = new VirtualDesktopViewModel(desktop);
            bool isHidden = ConfigHelper.Get(desktop.Id);
            vm.ShowcaseMessage = isHidden ? "Icons hidden" : "";
            if (desktop.Id == currentId)
            {
                vm.IsCurrent = true;
                IconsToggle.Content = isHidden ? "Show Icons" : "Hide Icons";
                FileHiderHelper.SetHiddenFiles(isHidden);
            }

            this.Desktops.Add(vm);
        }
        
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        this._applicationViewChangedListener = VirtualDesktop.RegisterViewChanged(this.GetHandle(), handle =>
        {
            this.Dispatcher.Invoke(() =>
            {
                var parent = VirtualDesktop.FromHwnd(handle);
                foreach (var desktop in this.Desktops)
                {
                    // Do something on initialization
                }
            });
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        this._applicationViewChangedListener?.Dispose();
        base.OnClosed(e);
    }

    private void CreateNew(object sender, RoutedEventArgs e)
        => VirtualDesktop.Create();

    private void Remove(object sender, RoutedEventArgs e)
    {
        VirtualDesktop.Current.Remove();
    }

    private void SwitchDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: VirtualDesktopViewModel vm })
        {
            VirtualDesktop.FromId(vm.Id)?.SwitchAndMove(this);
        }
    }

    private void ChangeWallpaper(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: VirtualDesktopViewModel vm })
        {
            var dialog = new OpenFileDialog()
            {
                Title = "Select wallpaper",
                Filter = "Desktop wallpaper (*.jpg, *.png, *.bmp)|*.jpg;*.png;*.bmp",
            };

            if ((dialog.ShowDialog(this) ?? false)
                && File.Exists(dialog.FileName))
            {
                var desktop = VirtualDesktop.FromId(vm.Id);
                if (desktop != null) desktop.WallpaperPath = dialog.FileName;
            }
        }

        e.Handled = true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void IconsToggle_Click(object sender, RoutedEventArgs e)
    {
        bool newHiddenState = !ConfigHelper.Get(currentId);
        var model = Desktops.FirstOrDefault((model) => model.Id == currentId);
        if (model == null) return;
        model.ShowcaseMessage = newHiddenState ? "Icons hidden" : "";
        SetIconsToggleText(newHiddenState);
        FileHiderHelper.SetHiddenFiles(newHiddenState);
        ConfigHelper.Set(currentId, newHiddenState);
    }

    private void SetIconsToggleText(bool state)
    {
        Dispatcher.Invoke(() =>
        {
            IconsToggle.Content = state ? "Show Icons" : "Hide Icons";
        });
    }
}

static class ConfigHelper
{
    private const string configFilename = "user.config";
    private static readonly HashSet<Guid> hiddenIconMap = new();

    public static void Load()
    {
        foreach (var line in ReadConfigLines())
        {
            if (Guid.TryParse(line, out var id))
            {
                hiddenIconMap.Add(id);
            }
        }
    }

    public static bool Get(Guid id)
    {
        return hiddenIconMap.Contains(id);
    }

    public static void Set(Guid id, bool hidden)
    {
        if (hidden)
        {
            AddToConfig(id);
            hiddenIconMap.Add(id);
        }
        else
        {
            RemoveFromConfig(id);
            hiddenIconMap.Remove(id);
        }
    }

    public static void Remove(Guid id)
    {
        RemoveFromConfig(id);
        hiddenIconMap.Remove(id);
    }

    private static void AddToConfig(Guid id)
    {
        var input = ReadConfigLines().ToList();
        bool alreadyExists = input.Any(line => line.Trim() == id.ToString());
        if (!alreadyExists)
        {
            input.Add(id.ToString());
            File.WriteAllLines(configFilename, input);
            Debug.WriteLine($"Added {id} to config");
            return;
        }
        Debug.WriteLine($"Add skipped, {id} already in config");
    }

    private static void RemoveFromConfig(Guid id)
    {
        var input = ReadConfigLines();
        var result = input
            .Where(line => line.Trim() != id.ToString())
            .ToList();
        if (result.Count < input.Length)
        {
            File.WriteAllLines(configFilename, result);
            Debug.WriteLine($"Removed {id} from config");
            return;
        }
        Debug.WriteLine($"Remove failed, didn't find {id} in config");
    }

    private static string[] ReadConfigLines()
    {
        if (!File.Exists(configFilename))
        {
            File.Create(configFilename).Close();
            return Array.Empty<string>();
        }
        return File.ReadAllLines(configFilename);
    }
}

static class FileHiderHelper
{
    private static bool? lastState = null;

    public static void SetHiddenFiles(bool hidden)
    {
        if (lastState == hidden) return;
        lastState = hidden;
        var userName = Environment.GetEnvironmentVariable("USERNAME") ?? throw new Exception("Couldn't get %USERNAME% environment variable");
        SetDirectoryFilesHidden($"C:\\Users\\{userName}\\Desktop", hidden);
        SetDirectoryFilesHidden($"C:\\Users\\Public\\Desktop", hidden);
        RefreshDesktop();
    }

    private static void SetDirectoryFilesHidden(string path, bool hidden)
    {
        var parentInfo = new DirectoryInfo(path);
        if (!parentInfo.Exists)
        {
            Debug.WriteLine($"Still couldn't find {path}");
            return;
        }

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(path))
            {
                if (!File.Exists(filePath)) throw new Exception($"Couldn't find file {filePath}");
                if (filePath.EndsWith("desktop.ini")) continue; // Don't modify desktop.ini

                var attributes = File.GetAttributes(filePath);
                File.SetAttributes(filePath, SetHiddenAttribute(attributes, hidden));
            }
            foreach (var dirPath in Directory.EnumerateDirectories(path))
            {
                var info = new DirectoryInfo(dirPath);
                if (!info.Exists) throw new Exception($"Couldn't find file {dirPath}");

                info.Attributes = SetHiddenAttribute(info.Attributes, hidden);
            }
        }
        catch (UnauthorizedAccessException e)
        {
            Debug.WriteLine(e);
            return;
        }
    }

    private static FileAttributes SetHiddenAttribute(FileAttributes attributes, bool hidden)
    {
        if (hidden)
        {
            attributes |= FileAttributes.Hidden;
        }
        else
        {
            attributes &= ~FileAttributes.Hidden;
        }
        return attributes;
    }

    private static void RefreshDesktop()
    {
        SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("Shell32.dll")]
    private static extern int SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
