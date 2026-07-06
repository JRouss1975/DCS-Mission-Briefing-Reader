using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Windows.Input;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DCSMissionReader
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _currentFolderPath = "";
        public string CurrentFolderPath
        {
            get => _currentFolderPath;
            set
            {
                if (_currentFolderPath != value)
                {
                    _currentFolderPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _numberOfMissions;
        public int NumberOfMissions
        {
            get => _numberOfMissions;
            set
            {
                if (_numberOfMissions != value)
                {
                    _numberOfMissions = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private string _missionDate = "";
        public string Date { get => _missionDate; set { _missionDate = value; OnPropertyChanged(); } }

        private string _missionStartTime = "";
        public string StartTime { get => _missionStartTime; set { _missionStartTime = value; OnPropertyChanged(); } }

        private string _missionSortie = "";
        public string Sortie { get => _missionSortie; set { _missionSortie = value; OnPropertyChanged(); } }

        private string _missionTheatre = "";
        private string _currentMissionPath = "";
        public string CurrentMissionPath { get => _currentMissionPath; set { _currentMissionPath = value; OnPropertyChanged(); } }
        public string Theatre { get => _missionTheatre; set { _missionTheatre = value; OnPropertyChanged(); } }
        
        private WeatherInfo? _weather;
        public WeatherInfo? Weather { get => _weather; set { _weather = value; OnPropertyChanged(); OnPropertyChanged(nameof(WeatherStringGround)); OnPropertyChanged(nameof(WeatherString2000)); OnPropertyChanged(nameof(WeatherString8000)); } }
        
        public string WeatherStringGround => Weather != null ? $"{Weather.WindSpeedGround} m/s @ {Weather.WindDirGround}°" : "N/A";
        public string WeatherString2000 => Weather != null ? $"{Weather.WindSpeed2000} m/s @ {Weather.WindDir2000}°" : "N/A";
        public string WeatherString8000 => Weather != null ? $"{Weather.WindSpeed8000} m/s @ {Weather.WindDir8000}°" : "N/A";

        private string _unitStatsString = "";
        public string UnitStatsString { get => _unitStatsString; set { _unitStatsString = value; OnPropertyChanged(); } }

        private ObservableCollection<PlayableUnitCount> _playableUnitCounts = new();
        public ObservableCollection<PlayableUnitCount> PlayableUnitCounts { get => _playableUnitCounts; set { _playableUnitCounts = value; OnPropertyChanged(); } }

        public bool IsMissionCopied => _copiedMissionPaths.Count > 0;

        public string AppVersion => "v1.1e";

        public string AppTitle => $"DCS MISSION BRIEFING READER {AppVersion}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Map state
        private MissionDetails? _currentMissionDetails;
        private string? _currentTheater;
        private string? _loadingMissionPath; // Track currently loading mission to prevent race conditions
        
        // GMap Markers

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Title = AppTitle;
            LoadSettings();
            InitializeGMap();
            InitializeSearch();
        }

        private AppSettings _settings = AppSettings.Load();

        private void LoadSettings()
        {
            _settings = AppSettings.Load();
            if (!string.IsNullOrEmpty(_settings.LastFolderPath) && Directory.Exists(_settings.LastFolderPath))
            {
                CurrentFolderPath = _settings.LastFolderPath;
                IncludeSubfoldersCheckBox.IsChecked = _settings.IncludeSubfolders;
                _includeSubfolders = _settings.IncludeSubfolders;
                LoadMissionFiles(_settings.LastFolderPath);
            }
        }

        private void InitializeGMap()
        {
            try
            {
                // Configure GMap HTTP headers to follow OpenStreetMap tile usage policy by using a unique custom User-Agent
                GMapProvider.UserAgent = "DCSMissionBriefingReader/1.1 (contact@dcsmissionbriefingreader.org; local-app)";

                // Configure GMap
                MainMap.MapProvider = GMapProviders.OpenStreetMap;
                MainMap.MapProvider.RefererUrl = "https://www.openstreetmap.org/";

                // Take full control over tile HTTP requests to ensure OSM policy compliance
                GMapProvider.WebRequestFactory = (provider, url) =>
                {
                    // OSM now requires HTTPS; upgrade any HTTP tile URLs
                    if (url.StartsWith("http://"))
                        url = "https://" + url.Substring(7);

#pragma warning disable SYSLIB0014
                    var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
                    request.UserAgent = GMapProvider.UserAgent;
                    request.Referer = "https://www.openstreetmap.org/";
                    request.Accept = "image/png,*/*";
                    request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                    request.Timeout = GMapProvider.TimeoutMs > 0 ? GMapProvider.TimeoutMs : 5000;
                    return request;
                };

                MainMap.Position = new PointLatLng(42.35, 43.32); // Center on Caucasus
                MainMap.MinZoom = 2;
                MainMap.MaxZoom = 18;
                MainMap.Zoom = 7;
                MainMap.ShowCenter = false;
                MainMap.DragButton = MouseButton.Left;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GMap Initialization Error: {ex.Message}");
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog openFolderDialog = new OpenFolderDialog();
            openFolderDialog.Multiselect = false;
            openFolderDialog.Title = "Select Missions Folder";

            if (openFolderDialog.ShowDialog() == true)
            {
                CurrentFolderPath = openFolderDialog.FolderName;
                _settings.LastFolderPath = CurrentFolderPath;
                _settings.Save();
                LoadMissionFiles(CurrentFolderPath);
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWin = new AboutWindow();
            aboutWin.Owner = this;
            aboutWin.ShowDialog();
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime Date, long Size, string Theater, string MainUnit)> _missionCache = new();

        private static IEnumerable<string> SafeEnumerateFiles(string rootDir, string pattern, bool includeSubfolders)
        {
            if (!Directory.Exists(rootDir))
                yield break;

            // Search current directory
            string[] topFiles;
            try
            {
                topFiles = Directory.GetFiles(rootDir, pattern, SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                topFiles = [];
            }
            foreach (var f in topFiles)
                yield return f;

            if (!includeSubfolders)
                yield break;

            // Recurse into subdirectories
            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(rootDir);
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var dir in subDirs)
            {
                foreach (var f in SafeEnumerateFiles(dir, pattern, true))
                    yield return f;
            }
        }

        private async void LoadMissionFiles(string folderPath, int focusIndex = -1)
        {
            try
            {
                MissionFilesListBox.ItemsSource = null;

                string[] files = SafeEnumerateFiles(folderPath, "*.miz", _includeSubfolders).ToArray();

                var pendingMissions = new List<MissionFile>();
                var missionsToParse = new List<MissionFile>();

                // Persistent index doubles as a cache of theatre/main unit across sessions
                var persistedCache = await Task.Run(() => SearchIndex.GetBriefCache());

                foreach (string file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var mission = new MissionFile
                    {
                        FileName = System.IO.Path.GetFileName(file),
                        FullPath = file,
                        FileDate = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length
                    };

                    // Check session cache first to avoid re-parsing ZIP files
                    if (_missionCache.TryGetValue(file, out var cachedData) &&
                        cachedData.Date == mission.FileDate &&
                        cachedData.Size == mission.FileSize)
                    {
                        mission.Theater = cachedData.Theater;
                        mission.MainUnit = cachedData.MainUnit;
                    }
                    else if (persistedCache.TryGetValue(file, out var persisted) &&
                             persisted.Size == mission.FileSize &&
                             persisted.MTime == fileInfo.LastWriteTimeUtc.Ticks &&
                             !string.IsNullOrEmpty(persisted.Theatre))
                    {
                        mission.Theater = persisted.Theatre;
                        mission.MainUnit = persisted.MainUnit;
                        _missionCache[file] = (mission.FileDate, mission.FileSize, mission.Theater, mission.MainUnit);
                    }
                    else
                    {
                        mission.Theater = "Loading...";
                        mission.MainUnit = "Loading...";
                        missionsToParse.Add(mission);
                    }

                    pendingMissions.Add(mission);
                }

                // Only parse missions that were NOT found in the cache
                if (missionsToParse.Count > 0)
                {
                    using var semaphore = new System.Threading.SemaphoreSlim(Math.Max(Environment.ProcessorCount * 2, 8));
                    
                    var tasks = missionsToParse.Select(async mission =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var info = await MizParser.GetMissionBriefInfoAsync(mission.FullPath);
                            mission.Theater = info.Theatre;
                            mission.MainUnit = info.MainUnit;

                            // Save to cache for next time
                            _missionCache[mission.FullPath] = (mission.FileDate, mission.FileSize, mission.Theater, mission.MainUnit);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"LoadMissionFiles parse failed for {mission.FullPath}: {ex.Message}");
                            mission.Theater = "Unknown";
                            mission.MainUnit = "Unknown";
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToArray();

                    await Task.WhenAll(tasks);
                }

                // Now that all parsing is completed, bind to UI
                var missionFiles = new ObservableCollection<MissionFile>(pendingMissions);
                _missionFilesCollection = missionFiles;
                _missionFilesView = CollectionViewSource.GetDefaultView(missionFiles);
                MissionFilesListBox.ItemsSource = _missionFilesView;
                NumberOfMissions = missionFiles.Count;

                ApplyGrouping();

                // Build/refresh the persistent full-text search index in the background
                StartBackgroundIndexing(files);

                if (focusIndex >= 0 && MissionFilesListBox.Items.Count > 0)
                {
                    int index = Math.Min(focusIndex, MissionFilesListBox.Items.Count - 1);
                    MissionFilesListBox.SelectedIndex = index;
                    MissionFilesListBox.ScrollIntoView(MissionFilesListBox.SelectedItem);
                }
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Error loading files: {ex.Message}", showCancel: false);
            }
        }

        private string _lastSortProperty = "FileName";
        private ListSortDirection _lastSortDirection = ListSortDirection.Ascending;
        private bool _isInternalChange = false;

        private void ApplyGrouping()
        {
            if (_missionFilesView == null) return;

            // Clear expansion states when grouping changes
            _theaterExpansionStates.Clear();

            _missionFilesView.GroupDescriptions.Clear();
            
            bool isGrouped = false;
    
    // Check which grouping is active. Order matters for group hierarchy (Theater -> Unit)
    if (GroupByTheaterCheckBox.IsChecked == true)
    {
        _missionFilesView.GroupDescriptions.Add(new PropertyGroupDescription("Theater"));
        isGrouped = true;
    }
    
    if (GroupByUnitCheckBox.IsChecked == true)
    {
        _missionFilesView.GroupDescriptions.Add(new PropertyGroupDescription("MainUnit"));
        isGrouped = true;
    }        

            // When grouping is active, disable virtualization so ALL expanders
            // are rendered and can be toggled with a single Expand/Collapse All click.
            // When ungrouped, re-enable virtualization for performance.
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(MissionFilesListBox, !isGrouped);
            VirtualizingPanel.SetIsVirtualizing(MissionFilesListBox, !isGrouped);

            // CanContentScroll=True (logical/item scrolling) only works well with virtualization.
            // When grouping is on (virtualization off), use False for smooth pixel-based scrolling.
            ScrollViewer.SetCanContentScroll(MissionFilesListBox, !isGrouped);

            // Re-apply sorting to update headers order (keep relevance order while a search is active)
            if (_searchActive)
                ApplySearchSorting();
            else
                ApplySorting(_lastSortProperty, _lastSortDirection);
        }

        private Dictionary<string, bool> _theaterExpansionStates = new Dictionary<string, bool>();

        private string GetGroupKey(CollectionViewGroup group)
        {
            if (group.IsBottomLevel && group.Items.Count > 0 && group.Items[0] is MissionFile mission)
            {
                // In "Both" mode, theaters are unique, units are not.
                // We identify the unit subgroup by combining it with the theater name.
                if (GroupByTheaterCheckBox.IsChecked == true && GroupByUnitCheckBox.IsChecked == true)
                {
                    return $"{mission.Theater}|{group.Name}";
                }
            }
            return group.Name?.ToString() ?? "Unknown";
        }

        private void Expander_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Expander expander && e.NewValue is CollectionViewGroup group)
            {
                string key = GetGroupKey(group);
                if (_theaterExpansionStates.TryGetValue(key, out bool isExpanded))
                {
                    _isInternalChange = true;
                    expander.IsExpanded = isExpanded;
                    _isInternalChange = false;
                }
                else
                {
                    // Default to expanded and store it
                    _isInternalChange = true;
                    expander.IsExpanded = true;
                    _theaterExpansionStates[key] = true;
                    _isInternalChange = false;
                }
            }
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            if (_isInternalChange) return;
            if (sender is Expander expander && e.Source == sender && expander.DataContext is CollectionViewGroup group)
            {
                _theaterExpansionStates[GetGroupKey(group)] = true;
            }
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            if (_isInternalChange) return;
            if (sender is Expander expander && e.Source == sender && expander.DataContext is CollectionViewGroup group)
            {
                _theaterExpansionStates[GetGroupKey(group)] = false;
            }
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e) => SetAllExpanders(true);
        private void CollapseAllButton_Click(object sender, RoutedEventArgs e) => SetAllExpanders(false);

        private void SetAllExpanders(bool isExpanded)
        {
            if (_missionFilesView == null) return;

            // 1. Update the state for all groups in the data view so newly virtualized expanders pick it up
            UpdateGroupsExpansionState(_missionFilesView.Groups, isExpanded);

            _isInternalChange = true;
            // 2. Update currently visible expanders
            var expanders = FindVisualChildren<Expander>(MissionFilesListBox);
            foreach (var expander in expanders)
            {
                expander.IsExpanded = isExpanded;
            }
            _isInternalChange = false;
        }

        private void UpdateGroupsExpansionState(IEnumerable? groups, bool isExpanded)
        {
            if (groups == null) return;
            foreach (var item in groups)
            {
                if (item is CollectionViewGroup group)
                {
                    _theaterExpansionStates[GetGroupKey(group)] = isExpanded;
                    if (!group.IsBottomLevel)
                    {
                        UpdateGroupsExpansionState(group.Items, isExpanded);
                    }
                }
            }
        }

        private void ExpandTheaterSubgroups_Click(object sender, RoutedEventArgs e) => SetTheaterSubgroupsExpansion(sender, true);
        private void CollapseTheaterSubgroups_Click(object sender, RoutedEventArgs e) => SetTheaterSubgroupsExpansion(sender, false);

        private void SetTheaterSubgroupsExpansion(object sender, bool isExpanded)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CollectionViewGroup group)
            {
                // 1. Update state in the dictionary for all children
                UpdateGroupsExpansionState(group.Items, isExpanded);

                // 2. Local UI update: Ensure parent theater is expanded and visible children match
                _isInternalChange = true;
                var parentExpander = FindParent<Expander>(fe);
                if (parentExpander != null)
                {
                    parentExpander.IsExpanded = true;
                    _theaterExpansionStates[GetGroupKey(group)] = true;

                    var children = FindVisualChildren<Expander>(parentExpander);
                    foreach (var child in children) child.IsExpanded = isExpanded;
                }
                _isInternalChange = false;
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject? child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T t) yield return t;
                foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
            }
        }

        private void BriefingExpander_Expanded(object sender, RoutedEventArgs e)
        {
            UpdateBriefingSectionRows();
        }

        private void BriefingExpander_Collapsed(object sender, RoutedEventArgs e)
        {
            UpdateBriefingSectionRows();
        }

        private void UpdateBriefingSectionRows()
        {
            if (BriefingSectionsGrid == null) return;

            var expanders = BriefingSectionsGrid.Children.OfType<Expander>().ToList();
            foreach (var expander in expanders)
            {
                if (int.TryParse(expander.Tag?.ToString(), out int rowIndex) && 
                    rowIndex < BriefingSectionsGrid.RowDefinitions.Count)
                {
                    BriefingSectionsGrid.RowDefinitions[rowIndex].Height = expander.IsExpanded 
                        ? new GridLength(1, GridUnitType.Star) 
                        : GridLength.Auto;
                }
            }
        }

        private void ExpandAllTasksButton_Click(object sender, RoutedEventArgs e) => SetAllBriefingExpanders(true);
        private void CollapseAllTasksButton_Click(object sender, RoutedEventArgs e) => SetAllBriefingExpanders(false);

        private void SetAllBriefingExpanders(bool isExpanded)
        {
            if (BriefingSectionsGrid == null) return;
            
            var expanders = BriefingSectionsGrid.Children.OfType<Expander>().ToList();
            foreach (var expander in expanders)
            {
                expander.IsExpanded = isExpanded;
            }
            UpdateBriefingSectionRows();
        }

        private ICollectionView? _missionFilesView;

        private async void MissionFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    MissionFilesListBox.ScrollIntoView(mission);
                    var container = MissionFilesListBox.ItemContainerGenerator.ContainerFromItem(mission) as FrameworkElement;
                    container?.BringIntoView();
                }), DispatcherPriority.Loaded);

                string fullPath = mission.FullPath;
                CurrentMissionPath = fullPath;
                _loadingMissionPath = fullPath; // Track which mission we're loading
                
                // Clear all text fields to prevent data from previous missions from persisting
                BriefingTextBlock.Text = "Loading...";
                BlueTaskTextBlock.Text = "";
                RedTaskTextBlock.Text = "";
                NeutralsTaskTextBlock.Text = "";
                Sortie = "";
                Date = "";
                StartTime = "";
                Theatre = "";
                Weather = null;
                
                FlightsDataGrid.ItemsSource = null;
                UnitStatsString = "Loading...";
                PlayableUnitCounts.Clear();
                ClearMapOverlays();
                
                try
                {
                    var details = await MizParser.ParseMissionAsync(fullPath);
                    
                    // Check if this mission is still the one we want to display (user might have switched to another)
                    if (_loadingMissionPath != fullPath)
                    {
                        // User has selected a different mission while this one was loading, discard these results
                        return;
                    }
                    
                    _currentMissionDetails = details;
                    
                    // Load all four briefing sections
                    BriefingTextBlock.Text = details.BriefingSituation ?? "";
                    BlueTaskTextBlock.Text = details.BriefingBlueTask ?? "";
                    RedTaskTextBlock.Text = details.BriefingRedTask ?? "";
                    NeutralsTaskTextBlock.Text = details.BriefingNeutralsTask ?? "";
                    
                    Date = details.Date;
                    StartTime = details.StartTime;
                    Sortie = details.Sortie ?? "";
                    Theatre = details.Theatre;
                    Weather = details.Weather;
                    FlightsDataGrid.ItemsSource = details.FlightSlots;
                    
                    UnitStatsString = $"{details.FlightSlots?.Count ?? 0} units in {details.AllGroups?.Count ?? 0} groups";
                    
                    var playable = details.FlightSlots?
                        .Where(u => u.Skill == "Client" || u.Skill == "Player")
                        .GroupBy(u => u.Type)
                        .Select(g => new PlayableUnitCount { Type = g.Key, Count = g.Count() })
                        .OrderByDescending(g => g.Count)
                        .ToList();
                    PlayableUnitCounts = new ObservableCollection<PlayableUnitCount>(playable ?? new List<PlayableUnitCount>());
                    
                    // Populate required mods list
                    if (details.RequiredModules != null && details.RequiredModules.Count > 0)
                    {
                        RequiredModsListBox.ItemsSource = details.RequiredModules;
                        RequiredModsListBox.Visibility = Visibility.Visible;
                        NoModsText.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        RequiredModsListBox.ItemsSource = null;
                        RequiredModsListBox.Visibility = Visibility.Collapsed;
                        NoModsText.Visibility = Visibility.Visible;
                    }

                    // Draw full mission map with OSM background
                    await DrawMissionMapAsync();

                    // Images
                    var images = new ObservableCollection<BitmapImage>();
                    if (details.Images != null)
                    {
                        foreach (var imgData in details.Images)
                        {
                            try
                            {
                                var image = new BitmapImage();
                                using (var ms = new MemoryStream(imgData))
                                {
                                    image.BeginInit();
                                    image.CacheOption = BitmapCacheOption.OnLoad;
                                    image.StreamSource = ms;
                                    image.EndInit();
                                }
                                image.Freeze();
                                images.Add(image);
                            }
                            catch (Exception ex) { Debug.WriteLine($"Image load failed: {ex.Message}"); }
                        }
                    }
                    ImagesItemsControl.ItemsSource = images;
                    
                    // Kneeboard Images
                    var kneeboardImages = new ObservableCollection<BitmapImage>();
                    if (details.KneeboardImages != null)
                    {
                        foreach (var imgData in details.KneeboardImages)
                        {
                            try
                            {
                                var image = new BitmapImage();
                                using (var ms = new MemoryStream(imgData))
                                {
                                    image.BeginInit();
                                    image.CacheOption = BitmapCacheOption.OnLoad;
                                    image.StreamSource = ms;
                                    image.EndInit();
                                }
                                image.Freeze();
                                kneeboardImages.Add(image);
                            }
                            catch (Exception ex) { Debug.WriteLine($"Kneeboard image load failed: {ex.Message}"); }
                        }
                    }
                    KneeboardItemsControl.ItemsSource = kneeboardImages;
                }
                catch (Exception ex)
                {
                    // Only show error if this is still the mission we're trying to load
                    if (_loadingMissionPath == fullPath)
                    {
                        ShowCustomDialog("Error", $"Error reading file: {ex.Message}", showCancel: false);
                        BriefingTextBlock.Text = "Failed to load briefing.";
                        BlueTaskTextBlock.Text = "";
                        RedTaskTextBlock.Text = "";
                        NeutralsTaskTextBlock.Text = "";
                        ImagesItemsControl.ItemsSource = null;
                        KneeboardItemsControl.ItemsSource = null;
                        RequiredModsListBox.ItemsSource = null;
                        RequiredModsListBox.Visibility = Visibility.Collapsed;
                        NoModsText.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        #region Map Drawing
        private void ClearMapOverlays()
        {
            MainMap.Markers.Clear();
        }

        private async Task DrawMissionMapAsync()
        {
            if (_currentMissionDetails == null) return;

            try
            {
            ClearMapOverlays();
            
            var groups = _currentMissionDetails.AllGroups ?? new List<UnitGroup>();
            _currentTheater = _currentMissionDetails.Theatre ?? "Caucasus";
            
            bool showBlue = ShowBlueCheckBox.IsChecked == true;
            bool showRed = ShowRedCheckBox.IsChecked == true;
            bool showRoutes = ShowRoutesCheckBox.IsChecked == true;
            bool showLabels = ShowLabelsCheckBox.IsChecked == true;

            // Draw routes and units
            int groupCount = 0; 
            foreach (var g in groups) 
            {
                bool show = (g.Coalition == "blue" && showBlue) || (g.Coalition == "red" && showRed) || (g.Coalition != "blue" && g.Coalition != "red");
                if (!show) continue;
                
                var color = g.Coalition == "blue" ? System.Windows.Media.Colors.CornflowerBlue : 
                           (g.Coalition == "red" ? System.Windows.Media.Colors.OrangeRed : System.Windows.Media.Colors.Gray);
                
                if (showRoutes && g.Route.Count > 1) DrawRoute(g.Route, color);
                foreach (var u in g.Units) DrawUnit(u, g.GroupType, g.Coalition, color, showLabels); 
                if (++groupCount % 20 == 0) await Task.Yield();
            }

            // Fit map to markers if we have any
            if (MainMap.Markers.Count > 0)
            {
                MainMap.ZoomAndCenterMarkers(null);
                if (MainMap.Zoom > 12) MainMap.Zoom = 12;
                if (MainMap.Zoom < 5) MainMap.Zoom = 5;
            }
            else
            {
                // Center on theater
                var (lat, lon) = MapHelper.GetTheaterCenter(_currentTheater);
                MainMap.Position = new PointLatLng(lat, lon);
                MainMap.Zoom = 7;
            }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Map rendering error: {ex.Message}");
            }
        }

        private PointLatLng DcsToLatLng(double dcsX, double dcsY)
        {
            // Use current theater or default to Caucasus if not set
            string theater = _currentTheater ?? "Caucasus";
            var (lat, lon) = MapHelper.DcsToLatLon(theater, dcsX, dcsY);
            return new PointLatLng(lat, lon);
        }

        private void DrawUnit(Unit unit, string groupType, string coalition, System.Windows.Media.Color color, bool showLabel)
        {
            var pos = DcsToLatLng(unit.X, unit.Y);
            
            // Validate position - skip invalid coordinates
            if (double.IsNaN(pos.Lat) || double.IsNaN(pos.Lng) || 
                double.IsInfinity(pos.Lat) || double.IsInfinity(pos.Lng) ||
                pos.Lat < -90 || pos.Lat > 90 || pos.Lng < -180 || pos.Lng > 180)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid position for unit {unit.Name}: DCS({unit.X}, {unit.Y}) -> ({pos.Lat}, {pos.Lng})");
                return;
            }
            
            var marker = new GMapMarker(pos);
            
            double size = unit.IsPlayer ? 16 : 12;
            Shape shape;
            
            if (groupType == "plane" || groupType == "helicopter")
            {
                // Triangle pointing up for aircraft
                shape = new Polygon
                {
                    Points = new PointCollection { 
                        new System.Windows.Point(size/2, 0), 
                        new System.Windows.Point(0, size), 
                        new System.Windows.Point(size, size) 
                    },
                    Fill = new SolidColorBrush(color),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1
                };
            }
            else if (groupType == "ship")
            {
                // Diamond for ships
                shape = new Polygon
                {
                    Points = new PointCollection { 
                        new System.Windows.Point(size/2, 0), 
                        new System.Windows.Point(size, size/2), 
                        new System.Windows.Point(size/2, size), 
                        new System.Windows.Point(0, size/2) 
                    },
                    Fill = new SolidColorBrush(color),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1
                };
            }
            else
            {
                // Rectangle for ground units
                shape = new Rectangle
                {
                    Width = size, 
                    Height = size,
                    Fill = new SolidColorBrush(color),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1
                };
            }

            marker.Shape = shape; 
            marker.Offset = new System.Windows.Point(-size/2, -size/2);
            
            string tooltipText = $"Name: {unit.Name ?? ""}\nType: {unit.Type}\nCoalition: {coalition}\nPos: ({pos.Lat:F4}, {pos.Lng:F4})";
            System.Windows.Controls.ToolTipService.SetToolTip(marker.Shape, tooltipText);
            
            MainMap.Markers.Add(marker);

            if (showLabel && !string.IsNullOrEmpty(unit.Type))
            {
                var labelMarker = new GMapMarker(pos);
                labelMarker.Shape = new TextBlock
                {
                    Text = unit.Type,
                    Foreground = new SolidColorBrush(color),
                    FontSize = 9,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(2, 0, 2, 0)
                };
                labelMarker.Offset = new System.Windows.Point(size/2 + 2, -6);
                System.Windows.Controls.ToolTipService.SetToolTip(labelMarker.Shape, tooltipText);
                MainMap.Markers.Add(labelMarker);
            }
        }

        private void DrawRoute(List<Waypoint> route, System.Windows.Media.Color color)
        {
            if (route.Count < 2) return;
            
            // First pass: collect valid waypoints (skip zeros and invalid values)
            var validWaypoints = new List<(Waypoint wp, PointLatLng pos)>();
            foreach (var wp in route)
            {
                // Skip waypoints at origin (0,0) - likely uninitialized
                if (Math.Abs(wp.X) < 1 && Math.Abs(wp.Y) < 1) continue;
                
                var pos = DcsToLatLng(wp.X, wp.Y);
                
                // Validate position
                if (!double.IsNaN(pos.Lat) && !double.IsNaN(pos.Lng) && 
                    !double.IsInfinity(pos.Lat) && !double.IsInfinity(pos.Lng) &&
                    pos.Lat >= -90 && pos.Lat <= 90 && pos.Lng >= -180 && pos.Lng <= 180)
                {
                    validWaypoints.Add((wp, pos));
                }
            }
            
            if (validWaypoints.Count < 2) return;
            
            // Calculate centroid and filter outliers
            double avgLat = validWaypoints.Average(w => w.pos.Lat);
            double avgLng = validWaypoints.Average(w => w.pos.Lng);
            
            // Calculate standard distance from centroid
            double maxDistanceFromCentroid = 5.0; // Maximum 5 degrees from centroid (~500km)
            
            var filteredPoints = new List<PointLatLng>();
            var filteredWaypoints = new List<Waypoint>();
            
            foreach (var (wp, pos) in validWaypoints)
            {
                double distFromCentroid = Math.Sqrt(Math.Pow(pos.Lat - avgLat, 2) + Math.Pow(pos.Lng - avgLng, 2));
                if (distFromCentroid <= maxDistanceFromCentroid)
                {
                    filteredPoints.Add(pos);
                    filteredWaypoints.Add(wp);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping outlier waypoint {wp.Name}: ({pos.Lat:F4}, {pos.Lng:F4}) - too far from centroid ({avgLat:F4}, {avgLng:F4})");
                }
            }
            
            if (filteredPoints.Count < 2) return;
            
            // Create route with custom styled path
            var groute = new GMapRoute(filteredPoints);
            
            // Set custom shape with thin solid line
            groute.Shape = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 5, 3 }
            };
            
            MainMap.Markers.Add(groute);
            
            // Add small waypoint markers
            for (int i = 0; i < filteredPoints.Count; i++)
            {
                var wpMarker = new GMapMarker(filteredPoints[i]);
                var wpDot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(color),
                    Stroke = System.Windows.Media.Brushes.White,
                    StrokeThickness = 1
                };
                wpMarker.Shape = wpDot;
                wpMarker.Offset = new System.Windows.Point(-5, -5);
                
                string wpName = i < filteredWaypoints.Count && filteredWaypoints[i].Name != null ? filteredWaypoints[i].Name : $"WP{i}";
                System.Windows.Controls.ToolTipService.SetToolTip(wpDot, $"Waypoint {i}: {wpName}");
                
                MainMap.Markers.Add(wpMarker);
            }
        }


        #endregion

        #region Map Controls

        private void ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ZoomAndCenterMarkers(null);
            if (MainMap.Zoom > 13) MainMap.Zoom = 13;
        }

        private async void MapOptionsChanged(object sender, RoutedEventArgs e)
        {
            if (_currentMissionDetails != null)
            {
                await DrawMissionMapAsync();
            }
        }

        private void GroupByTheaterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyGrouping();
        }

        private void GroupByUnitCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyGrouping();
        }

        private List<string> _copiedMissionPaths = new List<string>();
        private bool _includeSubfolders = true;

        private void MissionFilesListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) DeleteSelectedMission();
            else if (e.Key == Key.F2) RenameSelectedMission();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) CopySelectedMission();
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) PasteMission();
        }

        private void MenuItemCopy_Click(object sender, RoutedEventArgs e) => CopySelectedMission();
        private void MenuItemPaste_Click(object sender, RoutedEventArgs e) => PasteMission();
        private void MenuItemPasteToFolder_Click(object sender, RoutedEventArgs e) => PasteMissionToFolder();
        private void MenuItemOpenFolder_Click(object sender, RoutedEventArgs e) => OpenSelectedMissionFolder();
        private void MenuItemRename_Click(object sender, RoutedEventArgs e) => RenameSelectedMission();
        private void MenuItemDelete_Click(object sender, RoutedEventArgs e) => DeleteSelectedMission();

        private void OpenSelectedMissionFolder()
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission && File.Exists(mission.FullPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{mission.FullPath}\"");
        }

        private void CopySelectedMission()
        {
            var selectedMissions = MissionFilesListBox.SelectedItems.Cast<MissionFile>().ToList();
            if (selectedMissions.Count > 0)
            {
                _copiedMissionPaths = selectedMissions.Select(m => m.FullPath).ToList();
                OnPropertyChanged(nameof(IsMissionCopied));
            }
        }

        private void PasteMission() => PasteMissionInternal(CurrentFolderPath);

        private void PasteMissionToFolder()
        {
            if (_copiedMissionPaths.Count == 0) return;
            OpenFolderDialog openFolderDialog = new OpenFolderDialog { Multiselect = false, Title = "Select Destination Folder" };
            if (openFolderDialog.ShowDialog() == true)
                PasteMissionInternal(openFolderDialog.FolderName);
        }

        private void PasteMissionInternal(string targetDirectory)
        {
            if (_copiedMissionPaths.Count == 0) return;
            try
            {
                foreach (var sourcePath in _copiedMissionPaths)
                {
                    if (!File.Exists(sourcePath)) continue;

                    string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                    string extension = System.IO.Path.GetExtension(sourcePath);
                    string newFileName = System.IO.Path.GetFileName(sourcePath);
                    string newPath = System.IO.Path.Combine(targetDirectory, newFileName);

                    if (File.Exists(newPath))
                    {
                        newFileName = $"{fileNameWithoutExt} - Copy{extension}";
                        newPath = System.IO.Path.Combine(targetDirectory, newFileName);

                        int copyCount = 1;
                        while (File.Exists(newPath))
                        {
                            newFileName = $"{fileNameWithoutExt} - Copy ({++copyCount}){extension}";
                            newPath = System.IO.Path.Combine(targetDirectory, newFileName);
                        }
                    }

                    File.Copy(sourcePath, newPath);
                }

                bool isSubfolder = targetDirectory.StartsWith(CurrentFolderPath, StringComparison.OrdinalIgnoreCase);
                if (targetDirectory.Equals(CurrentFolderPath, StringComparison.OrdinalIgnoreCase) || (_includeSubfolders && isSubfolder))
                    LoadMissionFiles(CurrentFolderPath);
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Error pasting files: {ex.Message}", showCancel: false);
            }
        }

        private void DeleteSelectedMission()
        {
            var selectedMissions = MissionFilesListBox.SelectedItems.Cast<MissionFile>().ToList();
            if (selectedMissions.Count > 0)
            {
                int focusIndex = MissionFilesListBox.SelectedIndex;
                string message = selectedMissions.Count == 1
                    ? $"Delete mission '{selectedMissions[0].FileName}'?\nThis will remove the file from disk."
                    : $"Delete {selectedMissions.Count} missions?\nThis will remove the files from disk.";

                var result = ShowCustomDialog("Confirm Deletion", message, isConfirmation: true);
                if (result.Result == true)
                {
                    try
                    {
                        foreach (var mission in selectedMissions)
                        {
                            if (File.Exists(mission.FullPath))
                                File.Delete(mission.FullPath);
                        }
                        LoadMissionFiles(CurrentFolderPath, focusIndex);
                    }
                    catch (Exception ex)
                    {
                        ShowCustomDialog("Error", $"Error deleting files: {ex.Message}", showCancel: false);
                    }
                }
            }
        }

        private (bool? Result, string Input) ShowCustomDialog(string title, string message, string? defaultValue = null, bool showTextBox = false, bool isConfirmation = false, bool showCancel = true)
        {
            var dialog = new Window
            {
                Title = title.ToUpper(),
                Width = 450,
                SizeToContent = SizeToContent.Height,
                MinHeight = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (Brush)FindResource("DCS_DialogBackground"),
                Foreground = (Brush)FindResource("DCS_Text"),
                BorderBrush = (Brush)FindResource("DCS_Highlight"),
                BorderThickness = new Thickness(1)
            };

            var mainGrid = new System.Windows.Controls.Grid();
            var stackPanel = new StackPanel { Margin = new Thickness(25) };

            stackPanel.Children.Add(new TextBlock
            {
                Text = message,
                Margin = new Thickness(0, 0, 0, 20),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = (Brush)FindResource("DCS_Text")
            });

            TextBox? textBox = null;
            if (showTextBox)
            {
                textBox = new TextBox { 
                    Text = defaultValue, 
                    Margin = new Thickness(0, 0, 0, 20),
                    Padding = new Thickness(8, 5, 8, 5),
                    FontSize = 14
                };
                stackPanel.Children.Add(textBox);
                dialog.Loaded += (s, e) => {
                    textBox.Focus();
                    if (textBox.Text != null) textBox.SelectAll();
                };
            }

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            
            var okButton = new Button { 
                Content = (isConfirmation ? "YES" : "OK"), 
                Width = 90, 
                Height = 30,
                IsDefault = true, 
                Margin = new Thickness(10, 0, 0, 0) 
            };
            okButton.Click += (s, e) => dialog.DialogResult = true;
            buttonPanel.Children.Add(okButton);

            if (showCancel)
            {
                var cancelButton = new Button { 
                    Content = (isConfirmation ? "NO" : "CANCEL"), 
                    Width = 90, 
                    Height = 30,
                    Margin = new Thickness(10, 0, 0, 0), 
                    IsCancel = true 
                };
                cancelButton.Click += (s, e) => dialog.DialogResult = false;
                buttonPanel.Children.Add(cancelButton);
            }

            stackPanel.Children.Add(buttonPanel);
            mainGrid.Children.Add(stackPanel);
            dialog.Content = mainGrid;

            bool? result = dialog.ShowDialog();
            return (result, textBox?.Text ?? string.Empty);
        }

        private void RenameSelectedMission()
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                mission.IsEditing = true;
            }
        }

        private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.Visibility == Visibility.Visible)
            {
                textBox.Focus();
                string fileName = textBox.Text;
                if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".miz", StringComparison.OrdinalIgnoreCase))
                {
                    textBox.Select(0, fileName.Length - 4); // Select name without extension
                }
                else
                {
                    textBox.SelectAll();
                }
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MissionFile mission)
            {
                CommitRename(mission, textBox);
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is MissionFile mission)
            {
                if (e.Key == Key.Enter)
                {
                    CommitRename(mission, textBox);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    mission.IsEditing = false;
                    textBox.Text = mission.FileName; // revert
                    e.Handled = true;
                }
            }
        }

        private void CommitRename(MissionFile mission, TextBox textBox)
        {
            if (!mission.IsEditing) return; // Prevent multiple triggers
            
            mission.IsEditing = false;
            string newName = textBox.Text?.Trim() ?? string.Empty;
            
            if (!string.IsNullOrEmpty(newName) && newName != mission.FileName)
            {
                if (!newName.EndsWith(".miz", StringComparison.OrdinalIgnoreCase)) newName += ".miz";
                string? directory = System.IO.Path.GetDirectoryName(mission.FullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    string newPath = System.IO.Path.Combine(directory, newName);
                    try
                    {
                        if (File.Exists(newPath) && !newPath.Equals(mission.FullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            ShowCustomDialog("Error", "A file with that name already exists.", showCancel: false);
                            textBox.Text = mission.FileName;
                            return;
                        }
                        
                        File.Move(mission.FullPath, newPath);
                        LoadMissionFiles(CurrentFolderPath);
                    }
                    catch (Exception ex)
                    {
                        ShowCustomDialog("Error", $"Error renaming file: {ex.Message}", showCancel: false);
                        textBox.Text = mission.FileName;
                    }
                }
            }
            else
            {
                textBox.Text = mission.FileName; // Reset to original UI text
            }
        }

        private void IncludeSubfoldersCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _includeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true;
            _settings.IncludeSubfolders = _includeSubfolders;
            _settings.Save();
            if (!string.IsNullOrEmpty(CurrentFolderPath)) LoadMissionFiles(CurrentFolderPath);
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        #endregion

        #region Briefing Update

        private async void UpdateBriefingButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CurrentMissionPath) || !File.Exists(CurrentMissionPath))
            {
                ShowCustomDialog("Error", "No mission file is currently selected.", showCancel: false);
                return;
            }

            // Get all four briefing sections plus sortie
            string situationText = BriefingTextBlock.Text;
            string blueTaskText = BlueTaskTextBlock.Text;
            string redTaskText = RedTaskTextBlock.Text;
            string neutralsTaskText = NeutralsTaskTextBlock.Text;
            string sortieText = SortieTextBox.Text;
            var confirm = ShowCustomDialog("Confirm Save", $"Save all briefing changes to:\n{CurrentMissionPath}?\n\nThis will update Sortie, Situation, Blue Tasks, Red Tasks, and Neutrals sections.", isConfirmation: true);
            if (confirm.Result != true) return;

            try
            {
                await MizParser.UpdateAllBriefingsAsync(CurrentMissionPath, situationText, redTaskText, blueTaskText, neutralsTaskText, sortieText);
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Failed to update briefing: {ex.Message}", showCancel: false);
            }
        }

        #endregion

        #region Smart Search

        private ObservableCollection<MissionFile>? _missionFilesCollection;
        private DispatcherTimer? _searchDebounce;
        private CancellationTokenSource? _indexCts;
        private int _searchGeneration;
        private bool _searchActive;

        public ObservableCollection<string> SearchHistoryItems { get; } = new();
        private void InitializeSearch()
        {
            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounce.Tick += (s, e) => { _searchDebounce!.Stop(); _ = ExecuteSearchAsync(); };
            foreach (var q in _settings.SearchHistory)
                SearchHistoryItems.Add(q);
        }

        private void StartBackgroundIndexing(string[] files)
        {
            _indexCts?.Cancel();
            _indexCts = new CancellationTokenSource();
            var token = _indexCts.Token;
            _ = RunIndexingAsync(files, token);
        }

        private async Task RunIndexingAsync(string[] files, CancellationToken token)
        {
            try
            {
                var progress = new Progress<(int Done, int Total)>(p =>
                {
                    if (p.Total > 0)
                    {
                        IndexStatusText.Text = $"INDEXING MISSIONS {p.Done}/{p.Total}…";
                        IndexStatusText.Visibility = Visibility.Visible;
                    }
                });

                await SearchIndex.UpdateIndexAsync(files, progress, token);
                if (token.IsCancellationRequested) return;

                IndexStatusText.Visibility = Visibility.Collapsed;

                // Refresh an active search now that the index is up to date
                if (HasActiveQuery()) await ExecuteSearchAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                IndexStatusText.Text = $"INDEX ERROR: {ex.Message}";
                IndexStatusText.Visibility = Visibility.Visible;
            }
        }

        private bool HasActiveQuery()
        {
            return !string.IsNullOrWhiteSpace(SearchBox.Text);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = string.IsNullOrEmpty(SearchBox.Text);
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ScheduleSearch();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && SearchHistoryItems.Count > 0 && !SearchHistoryPopup.IsOpen)
            {
                SearchHistoryPopup.IsOpen = true;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (SearchHistoryPopup.IsOpen)
                {
                    SearchHistoryPopup.IsOpen = false;
                    e.Handled = true;
                }
            }
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !SearchHistoryPopup.IsOpen)
            {
                SearchBox.Text = "";
                e.Handled = true;
            }
        }

        private void SearchBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            SearchHistoryPopup.IsOpen = false;
        }

        private void SearchHistoryList_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is string selected)
            {
                SearchBox.Text = selected;
                SearchBox.CaretIndex = selected.Length;
                SearchHistoryPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            string path = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "DCS_Search_Guide.html");
            if (System.IO.File.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                    { UseShellExecute = true });
        }

        private void ScheduleSearch()
        {
            if (_searchDebounce == null) return;
            _searchDebounce.Stop();
            _searchDebounce.Start();
        }

        private async Task ExecuteSearchAsync()
        {
            if (_missionFilesView == null || _missionFilesCollection == null) return;

            int generation = ++_searchGeneration;

            if (!HasActiveQuery())
            {
                ClearSearchResults();
                return;
            }

            string searchText = SearchBox.Text.Trim();
            _settings.AddSearch(searchText);
            if (!SearchHistoryItems.Contains(searchText))
                SearchHistoryItems.Insert(0, searchText);
            while (SearchHistoryItems.Count > 20)
                SearchHistoryItems.RemoveAt(SearchHistoryItems.Count - 1);

            var request = new SearchRequest
            {
                Text = searchText,
                TagFilter = null,
                AircraftFilter = null,
                TimeFilter = null,
                LimitToPaths = new HashSet<string>(_missionFilesCollection.Select(m => m.FullPath), StringComparer.OrdinalIgnoreCase)
            };

            var results = await Task.Run(() => SearchIndex.Search(request));
            if (generation != _searchGeneration) return;

            var byPath = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results) byPath[r.Path] = r;

            foreach (var mission in _missionFilesCollection)
            {
                if (byPath.TryGetValue(mission.FullPath, out var r))
                {
                    mission.Score = r.Score;
                    string info = r.Tags.Length > 0 ? $"[{r.Tags}]" : "";
                    if (!string.IsNullOrEmpty(r.Snippet) && r.Snippet.Contains('«'))
                        info = info.Length > 0 ? info + "  " + r.Snippet : r.Snippet;
                    mission.MatchInfo = info;
                }
                else
                {
                    mission.Score = 0;
                    mission.MatchInfo = "";
                }
            }

            _searchActive = true;
            _missionFilesView.Filter = o => o is MissionFile m && byPath.ContainsKey(m.FullPath);
            ApplySearchSorting();

            int matched = _missionFilesCollection.Count(m => byPath.ContainsKey(m.FullPath));
            IndexStatusText.Text = $"{matched} MATCH{(matched == 1 ? "" : "ES")}";
            IndexStatusText.Visibility = Visibility.Visible;
        }

        private void ClearSearchResults()
        {
            _searchActive = false;
            if (_missionFilesView != null) _missionFilesView.Filter = null;
            if (_missionFilesCollection != null)
            {
                foreach (var mission in _missionFilesCollection)
                {
                    mission.Score = 0;
                    mission.MatchInfo = "";
                }
            }
            ApplySorting(_lastSortProperty, _lastSortDirection);
            IndexStatusText.Visibility = Visibility.Collapsed;
        }

        private void ApplySearchSorting()
        {
            if (_missionFilesView == null) return;
            _missionFilesView.SortDescriptions.Clear();
            if (GroupByTheaterCheckBox.IsChecked == true)
                _missionFilesView.SortDescriptions.Add(new SortDescription("Theater", ListSortDirection.Ascending));
            if (GroupByUnitCheckBox.IsChecked == true)
                _missionFilesView.SortDescriptions.Add(new SortDescription("MainUnit", ListSortDirection.Ascending));
            _missionFilesView.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            _missionFilesView.SortDescriptions.Add(new SortDescription("FileName", ListSortDirection.Ascending));
            _missionFilesView.Refresh();
        }

        private void MenuItemFindSimilar_Click(object sender, RoutedEventArgs e)
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                SearchBox.Text = $"like:\"{System.IO.Path.GetFileNameWithoutExtension(mission.FileName)}\"";
            }
        }

        #endregion

        #region Sorting

        private void SortAZButton_Click(object sender, RoutedEventArgs e) => ApplySorting("FileName", ListSortDirection.Ascending);

        private void SortZAButton_Click(object sender, RoutedEventArgs e) => ApplySorting("FileName", ListSortDirection.Descending);

        private void SortDateButton_Click(object sender, RoutedEventArgs e) => ApplySorting("FileDate", ListSortDirection.Descending);

        private void SortSizeButton_Click(object sender, RoutedEventArgs e) => ApplySorting("FileSize", ListSortDirection.Descending);

        private void ApplySorting(string propertyName, ListSortDirection direction)
        {
            _lastSortProperty = propertyName;
            _lastSortDirection = direction;

            if (_missionFilesView == null) return;

            _missionFilesView.SortDescriptions.Clear();
            
            // If grouping is active, sort the groups first
            if (GroupByTheaterCheckBox.IsChecked == true)
            {
                _missionFilesView.SortDescriptions.Add(new SortDescription("Theater", direction));
            }
            
            if (GroupByUnitCheckBox.IsChecked == true)
            {
                _missionFilesView.SortDescriptions.Add(new SortDescription("MainUnit", direction));
            }

            // Always add the primary property sort for the items within groups
            _missionFilesView.SortDescriptions.Add(new SortDescription(propertyName, direction));
            _missionFilesView.Refresh();
        }

        #endregion
    }
}