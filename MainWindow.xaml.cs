using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows.Input;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DCSMissionReader
{

    public class MissionFile : INotifyPropertyChanged
    {
        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        private string _theater;
        public string Theater
        {
            get => _theater;
            set { _theater = value; OnPropertyChanged(); }
        }

        public string FullPath { get; set; }
        
        public DateTime FileDate { get; set; }
        
        public long FileSize { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString() => FileName;
    }
    
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
        
        private string _missionDate;
        public string Date { get => _missionDate; set { _missionDate = value; OnPropertyChanged(); } }

        private string _missionStartTime;
        public string StartTime { get => _missionStartTime; set { _missionStartTime = value; OnPropertyChanged(); } }

        private string _missionSortie;
        public string Sortie { get => _missionSortie; set { _missionSortie = value; OnPropertyChanged(); } }

        private string _missionTheatre;
        private string _currentMissionPath;
        public string CurrentMissionPath { get => _currentMissionPath; set { _currentMissionPath = value; OnPropertyChanged(); } }
        public string Theatre { get => _missionTheatre; set { _missionTheatre = value; OnPropertyChanged(); } }
        
        private WeatherInfo _weather;
        public WeatherInfo Weather { get => _weather; set { _weather = value; OnPropertyChanged(); OnPropertyChanged(nameof(WeatherStringGround)); OnPropertyChanged(nameof(WeatherString2000)); OnPropertyChanged(nameof(WeatherString8000)); } }
        
        public string WeatherStringGround => Weather != null ? $"{Weather.WindSpeedGround} m/s @ {Weather.WindDirGround}°" : "N/A";
        public string WeatherString2000 => Weather != null ? $"{Weather.WindSpeed2000} m/s @ {Weather.WindDir2000}°" : "N/A";
        public string WeatherString8000 => Weather != null ? $"{Weather.WindSpeed8000} m/s @ {Weather.WindDir8000}°" : "N/A";

        public bool IsMissionCopied => !string.IsNullOrEmpty(_copiedMissionPath);

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Map state
        private MissionDetails _currentMissionDetails;
        private string _currentTheater;
        
        // GMap Markers

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            InitializeGMap();
        }

        private void InitializeGMap()
        {
            try
            {
                // Configure GMap
                MainMap.MapProvider = GMapProviders.OpenStreetMap;
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
                LoadMissionFiles(CurrentFolderPath);
            }
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWin = new AboutWindow();
            aboutWin.Owner = this;
            aboutWin.ShowDialog();
        }

        private async void LoadMissionFiles(string folderPath)
        {
            try
            {
                string[] files = _includeSubfolders 
                    ? Directory.GetFiles(folderPath, "*.miz", SearchOption.AllDirectories)
                    : Directory.GetFiles(folderPath, "*.miz", SearchOption.TopDirectoryOnly);
                
                var missionFiles = new ObservableCollection<MissionFile>();
                
                foreach (string file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var mission = new MissionFile 
                    { 
                        FileName = System.IO.Path.GetFileName(file),
                        FullPath = file,
                        Theater = "Loading...",
                        FileDate = fileInfo.LastWriteTime,
                        FileSize = fileInfo.Length
                    };
                    missionFiles.Add(mission);
                }

                _missionFilesView = CollectionViewSource.GetDefaultView(missionFiles);
                MissionFilesListBox.ItemsSource = _missionFilesView;
                NumberOfMissions = missionFiles.Count;

                foreach (var mission in missionFiles)
                {
                    try
                    {
                        string theatre = await MizParser.GetTheatreAsync(mission.FullPath);
                        mission.Theater = theatre;
                    }
                    catch
                    {
                        mission.Theater = "Unknown";
                    }
                }
                
                _missionFilesView.Refresh();
                ApplyGrouping();
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Error loading files: {ex.Message}", showCancel: false);
            }
        }

        private void ApplyGrouping()
        {
            if (_missionFilesView == null) return;

            _missionFilesView.GroupDescriptions.Clear();
            if (GroupByTheaterCheckBox.IsChecked == true)
            {
                _missionFilesView.GroupDescriptions.Add(new PropertyGroupDescription("Theater"));
            }
        }

        private Dictionary<string, bool> _theaterExpansionStates = new Dictionary<string, bool>();

        private void Expander_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
            {
                string theaterName = group.Name?.ToString() ?? "Unknown";
                if (_theaterExpansionStates.TryGetValue(theaterName, out bool isExpanded))
                    expander.IsExpanded = isExpanded;
                else
                    _theaterExpansionStates[theaterName] = expander.IsExpanded;
            }
        }

        private void Expander_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
                _theaterExpansionStates[group.Name?.ToString() ?? "Unknown"] = true;
        }

        private void Expander_Collapsed(object sender, RoutedEventArgs e)
        {
            if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
                _theaterExpansionStates[group.Name?.ToString() ?? "Unknown"] = false;
        }

        private void ExpandAllButton_Click(object sender, RoutedEventArgs e) => SetAllExpanders(true);
        private void CollapseAllButton_Click(object sender, RoutedEventArgs e) => SetAllExpanders(false);

        private void SetAllExpanders(bool isExpanded)
        {
            var expanders = FindVisualChildren<Expander>(MissionFilesListBox);
            foreach (var expander in expanders)
            {
                expander.IsExpanded = isExpanded;
                if (expander.DataContext is CollectionViewGroup group)
                    _theaterExpansionStates[group.Name?.ToString() ?? "Unknown"] = isExpanded;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T) yield return (T)child;
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

        private ICollectionView _missionFilesView;

        private async void MissionFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    MissionFilesListBox.ScrollIntoView(mission);
                    var container = MissionFilesListBox.ItemContainerGenerator.ContainerFromItem(mission) as FrameworkElement;
                    container?.BringIntoView();
                }), DispatcherPriority.Loaded);

                string fullPath = mission.FullPath;
                CurrentMissionPath = fullPath;
                BriefingTextBlock.Text = "Loading...";
                FlightsDataGrid.ItemsSource = null;
                ClearMapOverlays();
                
                try
                {
                    var details = await MizParser.ParseMissionAsync(fullPath);
                    _currentMissionDetails = details;
                    
                    // Load all four briefing sections
                    BriefingTextBlock.Text = details.BriefingSituation ?? "";
                    BlueTaskTextBlock.Text = details.BriefingBlueTask ?? "";
                    RedTaskTextBlock.Text = details.BriefingRedTask ?? "";
                    NeutralsTaskTextBlock.Text = details.BriefingNeutralsTask ?? "";
                    
                    Date = details.Date;
                    StartTime = details.StartTime;
                    SortieTextBox.Text = details.Sortie ?? "";
                    Theatre = details.Theatre;
                    Weather = details.Weather;
                    FlightsDataGrid.ItemsSource = details.FlightSlots;
                    
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
                            catch { }
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
                            catch { }
                        }
                    }
                    KneeboardItemsControl.ItemsSource = kneeboardImages;
                }
                catch (Exception ex)
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

        #region Map Drawing
        private void ClearMapOverlays()
        {
            MainMap.Markers.Clear();
        }

        private async Task DrawMissionMapAsync()
        {
            if (_currentMissionDetails == null) return;

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

        private PointLatLng DcsToLatLng(double dcsX, double dcsY)
        {
            var (lat, lon) = MapHelper.DcsToLatLon(_currentTheater, dcsX, dcsY);
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

        private void GroupByTheaterCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyGrouping();

        private string _copiedMissionPath = null;
        private bool _includeSubfolders = false;

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
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                var result = ShowCustomDialog("Copy Mission", $"Copy mission '{mission.FileName}' to clipboard?", isConfirmation: true);
                if (result.Result == true)
                {
                    _copiedMissionPath = mission.FullPath;
                    OnPropertyChanged(nameof(IsMissionCopied));
                }
            }
        }

        private void PasteMission() => PasteMissionInternal(CurrentFolderPath);

        private void PasteMissionToFolder()
        {
            if (string.IsNullOrEmpty(_copiedMissionPath) || !File.Exists(_copiedMissionPath)) return;
            OpenFolderDialog openFolderDialog = new OpenFolderDialog { Multiselect = false, Title = "Select Destination Folder" };
            if (openFolderDialog.ShowDialog() == true)
                PasteMissionInternal(openFolderDialog.FolderName);
        }

        private void PasteMissionInternal(string targetDirectory)
        {
            if (string.IsNullOrEmpty(_copiedMissionPath) || !File.Exists(_copiedMissionPath)) return;
            try
            {
                string fileName = System.IO.Path.GetFileName(_copiedMissionPath);
                var confirm = ShowCustomDialog("Confirm Paste", $"Paste '{fileName}' into '{targetDirectory}'?", isConfirmation: true);
                if (confirm.Result != true) return;

                string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(_copiedMissionPath);
                string extension = System.IO.Path.GetExtension(_copiedMissionPath);
                string newFileName = $"{fileNameWithoutExt} - Copy{extension}";
                string newPath = System.IO.Path.Combine(targetDirectory, newFileName);

                int copyCount = 1;
                while (File.Exists(newPath))
                {
                    newFileName = $"{fileNameWithoutExt} - Copy ({++copyCount}){extension}";
                    newPath = System.IO.Path.Combine(targetDirectory, newFileName);
                }

                File.Copy(_copiedMissionPath, newPath);

                bool isSubfolder = targetDirectory.StartsWith(CurrentFolderPath, StringComparison.OrdinalIgnoreCase);
                if (targetDirectory.Equals(CurrentFolderPath, StringComparison.OrdinalIgnoreCase) || (_includeSubfolders && isSubfolder))
                    LoadMissionFiles(CurrentFolderPath);
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Error pasting file: {ex.Message}", showCancel: false);
            }
        }

        private void DeleteSelectedMission()
        {
            if (MissionFilesListBox.SelectedItem is MissionFile mission)
            {
                var result = ShowCustomDialog("Confirm Deletion", $"Delete mission '{mission.FileName}'?\nThis will remove the file from disk.", isConfirmation: true);
                if (result.Result == true)
                {
                    try
                    {
                        File.Delete(mission.FullPath);
                        LoadMissionFiles(CurrentFolderPath);
                    }
                    catch (Exception ex)
                    {
                        ShowCustomDialog("Error", $"Error deleting file: {ex.Message}", showCancel: false);
                    }
                }
            }
        }

        private (bool? Result, string Input) ShowCustomDialog(string title, string message, string defaultValue = null, bool showTextBox = false, bool isConfirmation = false, bool showCancel = true)
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

            TextBox textBox = null;
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
                var dialogResult = ShowCustomDialog("Rename Mission", "Enter new name:", mission.FileName, showTextBox: true);
                if (dialogResult.Result == true)
                {
                    string newName = dialogResult.Input;
                    if (!string.IsNullOrEmpty(newName) && newName != mission.FileName)
                    {
                        if (!newName.EndsWith(".miz", StringComparison.OrdinalIgnoreCase)) newName += ".miz";
                        string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(mission.FullPath), newName);
                        try
                        {
                            File.Move(mission.FullPath, newPath);
                            LoadMissionFiles(CurrentFolderPath);
                        }
                        catch (Exception ex)
                        {
                            ShowCustomDialog("Error", $"Error renaming file: {ex.Message}", showCancel: false);
                        }
                    }
                }
            }
        }

        private void IncludeSubfoldersCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _includeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true;
            if (!string.IsNullOrEmpty(CurrentFolderPath)) LoadMissionFiles(CurrentFolderPath);
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

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
                ShowCustomDialog("Success", "All briefing sections updated successfully!", showCancel: false);
            }
            catch (Exception ex)
            {
                ShowCustomDialog("Error", $"Failed to update briefing: {ex.Message}", showCancel: false);
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
            if (_missionFilesView == null) return;

            _missionFilesView.SortDescriptions.Clear();
            _missionFilesView.SortDescriptions.Add(new SortDescription(propertyName, direction));
            _missionFilesView.Refresh();
        }

        #endregion
        #endregion
    }
}