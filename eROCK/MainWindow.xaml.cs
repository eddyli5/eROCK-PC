using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;

using Nordicsemi;
using Microsoft.Research.DynamicDataDisplay.DataSources;
using Microsoft.Research.DynamicDataDisplay;
using System.Windows.Threading;
using System.Globalization;

namespace eROCK
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        enum PipeSetup
        {
            NetworkAvailability = 1,
        }

        class StringValue
        {
            public string Text { get; private set; }
            public object Data { get; private set; }

            public StringValue(string val)
            {
                Text = val;
            }

            public StringValue(string val, object data)
            {
                Text = val;
                Data = data;
            }
        }

        class AppText
        {
            public const string StartingUp = "Starting up";
            public const string Ready = "Ready";
            public const string WorkerCompleted = "WorkerCompleted";
            public const string Connect = "Connect";
            public const string Disconnect = "Disconnect";
            public const string NoDeviceSelected = "No device is selected";
            public const string Close = "Close";
            public const string Open = "Open";
            public const string OperationFailed = "Operation failed";
        }

        MasterEmulator masterEmulator;
        BackgroundWorker initMasterEmulatorWorker = new BackgroundWorker();
        BindingList<StringValue> log = new BindingList<StringValue>();
        BindingList<StringValue> discoveredDevices = new BindingList<StringValue>();
        BindingList<RealtimeGraphItem> items = new BindingList<RealtimeGraphItem>();
        bool isPipeDiscoveryComplete = false;
        bool isOpen = false;
        bool isConnected = false;
        bool isRunning = false;
        //bool netWorkAvailable = false;
        int networkAvailabilityPipe;
        //int networkAvailabilityReqPipe;
        private DateTime last;
        private DateTime begin;
        static Int32 count = 0;

        double phase = 0;
        readonly double[] animatedX = new double[1000];
        readonly double[] animatedY = new double[1000];
        EnumerableDataSource<double> animatedDataSource = null;

        ObservableDataSource<Point> source1 = null;
        ObservableDataSource<Point> source2 = null;
        ObservableDataSource<Point> source3 = null;

        /// <summary>Programmatically created header</summary>
        Header chartHeader = new Header();

        /// <summary>Text contents of header</summary>
        TextBlock headerContents = new TextBlock();

        /// <summary>Timer to animate data</summary>
        readonly DispatcherTimer timer = new DispatcherTimer();


        public MainWindow()
        {
            InitializeComponent();
            //Graph.SeriesSource = items;
            last = DateTime.Now;
            begin = DateTime.Now;

            headerContents.FontSize = 24;
            headerContents.Text = "Phase = 0.00";
            headerContents.HorizontalAlignment = HorizontalAlignment.Center;
            chartHeader.Content = headerContents;
            plotter.Children.Add(chartHeader);
        }

        private void AnimatedPlot_Timer(object sender, EventArgs e)
        {
            phase += 0.01;
            if (phase > 2 * Math.PI)
                phase -= 2 * Math.PI;
            for (int i = 0; i < animatedX.Length; i++)
                animatedY[i] = Math.Sin(animatedX[i] + phase);

            // Here it is - signal that data is updated
            animatedDataSource.RaiseDataChanged();
            headerContents.Text = String.Format(CultureInfo.InvariantCulture, "Phase = {0:N2}", phase);
        }

        void RegisterEventHandlers()
        {
            masterEmulator.LogMessage += OnLogMessage;
            masterEmulator.DataReceived += OnDataReceived;
            masterEmulator.Connected += OnConnected;
            masterEmulator.Disconnected += OnDisconnected;
        }

        void RunWorkerInitMasterEmulator()
        {
            this.Cursor = Cursors.Wait;
            initMasterEmulatorWorker.DoWork += OnInitMasterEmulatorDoWork;
            initMasterEmulatorWorker.RunWorkerCompleted += OnInitMasterEmulatorCompleted;
            initMasterEmulatorWorker.RunWorkerAsync();
        }

        void AddLineToLog(String s)
        {
            tbLog.Text += s + Environment.NewLine;
            tbLog.ScrollToEnd();            
        }

        void ShowInTextbox(String s)
        {
            //tbTest.Text = s;
        }

        void OnInitMasterEmulatorDoWork(object sender, DoWorkEventArgs e)
        {
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                AddLineToLog(AppText.StartingUp);
            });

            masterEmulator = new MasterEmulator();
            RegisterEventHandlers();

            IEnumerable<string> usbDevices = masterEmulator.EnumerateUsb();

            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                PopulateUsbDevComboBox(usbDevices);
            });

            initMasterEmulatorWorker.DoWork -= OnInitMasterEmulatorDoWork;
        }

        void OnInitMasterEmulatorCompleted(object sender, RunWorkerCompletedEventArgs e)
        {

            this.Cursor = null;
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                if (e.Error != null)
                {
                    DisplayErrorMessage(e.Error);
                }
                else
                {
                    AddLineToLog(AppText.Ready);
                    btnOpenClose.IsEnabled = true;
                }
            });
            Debug.WriteLine(AppText.WorkerCompleted);
            initMasterEmulatorWorker.RunWorkerCompleted -= OnInitMasterEmulatorCompleted;
        }

        void PerformPipeSetup()
        {
            const ushort nwaServiceUUID = 0x180D;// 0x180B;
            const ushort nwaCharacteristicUuid = 0x2A37;// 0x2A3E;


            BtUuid serviceUuid1 = new BtUuid(nwaServiceUUID);
            PipeStore pipeStoreR = PipeStore.Remote;
            masterEmulator.SetupAddService(serviceUuid1, pipeStoreR);
            //masterEmulator.SetupAddService(serviceUuid1, PipeStore.Local);

            BtUuid charDefUuid1 = new BtUuid(nwaCharacteristicUuid);
            int maxDataLength = 20;
            byte[] data = new byte[] { 0 };
            masterEmulator.SetupAddCharacteristicDefinition(charDefUuid1, maxDataLength, data);

            networkAvailabilityPipe = masterEmulator.SetupAssignPipe(PipeType.Receive);// Transmit);
            //networkAvailabilityReqPipe = masterEmulator.SetupAssignPipe(PipeType.TransmitRequest);
        }
        /// <summary>
        /// By discovering pipes, the pipe setup we have specified will be matched up
        /// to the remote device's ATT table by ATT service discovery.
        /// </summary>
        void DiscoverPipes()
        {
            bool success = masterEmulator.DiscoverPipes();

            if (!success)
            {
                AddLineToLog("DiscoverPipes did not succeed.");
            }
        }

        /// <summary>
        /// Pipes of type PipeType.Receive must be opened before they will start receiving notifications.
        /// This maps to ATT Client Configuration Descriptors.
        /// </summary>
        void OpenRemotePipes()
        {
            var openedPipesEnumeration = masterEmulator.OpenAllRemotePipes();
            List<int> openedPipes = new List<int>(openedPipesEnumeration);
        }

        void DisplayErrorMessage(Exception ex)
        {
            MessageBox.Show(String.Format("{0}: {1}", AppText.OperationFailed, ex.Message));
            Debug.WriteLine(ex.StackTrace);
        }

        void OpenMasterEmulator(string usbSerial)
        {
            masterEmulator.Open(usbSerial);
            masterEmulator.Reset();
            isOpen = true;
            UpdateButtons();
        }

        void CloseMasterEmulator()
        {
            masterEmulator.Close();
            isOpen = false;
        }

        void Run()
        {
            try
            {
                masterEmulator.Run();
            }
            catch (Exception ex)
            {
                DisplayErrorMessage(ex);
                if (isOpen)
                {
                    CloseMasterEmulator();
                }
            }
        }

        void PopulateUsbDevComboBox(IEnumerable<string> devices)
        {
            foreach (string s in devices)
            {
                cboUsbSerial.Items.Add(s);
            }
            if (devices.Count() > 0)
            {
                cboUsbSerial.SelectedIndex = 0;
            }
        }

        #region master emulator event handlers

        void OnLogMessage(object sender, ValueEventArgs<string> e)
        {
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                StringValue s = new StringValue(e.Value);
                AddLineToLog(s.Text);

            });
        }

        void OnDataReceived(object sender, PipeDataEventArgs e)
        {
            StringBuilder stringBuffer = new StringBuilder();
            //short[] dataX = new short[] { e.PipeData[0], e.PipeData[6], e.PipeData[12] };
            //short[] dataY = new short[] { e.PipeData[2], e.PipeData[8], e.PipeData[14] };
            //short[] dataZ = new short[] { e.PipeData[4], e.PipeData[10], e.PipeData[16] };
            Int16[] dataX = new Int16[] { BitConverter.ToInt16(new byte[] { e.PipeData[0], e.PipeData[1] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[6], e.PipeData[7] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[12], e.PipeData[13] }, 0) };
            Int16[] dataY = new Int16[] { BitConverter.ToInt16(new byte[] { e.PipeData[2], e.PipeData[3] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[8], e.PipeData[9] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[14], e.PipeData[15] }, 0) };
            Int16[] dataZ = new Int16[] { BitConverter.ToInt16(new byte[] { e.PipeData[4], e.PipeData[5] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[10], e.PipeData[11] }, 0), BitConverter.ToInt16(new byte[] { e.PipeData[16], e.PipeData[17] }, 0) };

            foreach (byte element in e.PipeData)
            {
                stringBuffer.AppendFormat(" 0x{0:X2}", element);
            }

            TimeSpan span = DateTime.Now - begin;
            //int previousTime = items.Count > 0 ? items[items.Count - 1].Time : 0;

            //this.Dispatcher.BeginInvoke((Action)delegate ()
            //{
            //ShowInTextbox(stringBuffer.ToString());
            //RealtimeGraphItem newItem = new RealtimeGraphItem
            //{
            //    Time = (int)(previousTime + span.TotalMilliseconds),
            //    Value = dataX[0]
            //};
            //items.Add(newItem);
                double x = Convert.ToDouble(count++);// span.Milliseconds;
                double y1 = Convert.ToDouble(dataX[0]);
            double y2 = Convert.ToDouble(dataY[0]);
            double y3 = Convert.ToDouble(dataZ[0]);

            double x2 = Convert.ToDouble(count++);
            double y4 = Convert.ToDouble(dataX[1]);
            double y5 = Convert.ToDouble(dataY[1]);
            double y6 = Convert.ToDouble(dataZ[1]);
            double x3 = Convert.ToDouble(count++);
            double y7 = Convert.ToDouble(dataX[2]);
            double y8 = Convert.ToDouble(dataY[2]);
            double y9 = Convert.ToDouble(dataZ[2]);

            Point p1 = new Point(x, y1);
            Point p2 = new Point(x, y2);
            Point p3 = new Point(x, y3);

            Point p4 = new Point(x2, y4);
            Point p5 = new Point(x2, y5);
            Point p6 = new Point(x2, y6);
            Point p7 = new Point(x3, y7);
            Point p8 = new Point(x3, y8);
            Point p9 = new Point(x3, y9);

            source1.AppendAsync(Dispatcher, p1);
            source2.AppendAsync(Dispatcher, p2);
            source3.AppendAsync(Dispatcher, p3);

            source1.AppendAsync(Dispatcher, p4);
            source2.AppendAsync(Dispatcher, p5);
            source3.AppendAsync(Dispatcher, p6);

            source1.AppendAsync(Dispatcher, p7);
            source2.AppendAsync(Dispatcher, p8);
            source3.AppendAsync(Dispatcher, p9);

            //x = Convert.ToDouble(count++);
            //y1 = Convert.ToDouble(dataX[1]);
            //y2 = Convert.ToDouble(dataY[1]);
            //y3 = Convert.ToDouble(dataZ[1]);

            //p1 = new Point(x, y1);
            //p2 = new Point(x, y2);
            //p3 = new Point(x, y3);

            //source1.AppendAsync(Dispatcher, p1);
            //source2.AppendAsync(Dispatcher, p2);
            //source3.AppendAsync(Dispatcher, p3);

            //x = Convert.ToDouble(count++);
            //y1 = Convert.ToDouble(dataX[2]);
            //y2 = Convert.ToDouble(dataY[2]);
            //y3 = Convert.ToDouble(dataZ[2]);

            //p1 = new Point(x, y1);
            //p2 = new Point(x, y2);
            //p3 = new Point(x, y3);

            //source1.AppendAsync(Dispatcher, p1);
            //source2.AppendAsync(Dispatcher, p2);
            //source3.AppendAsync(Dispatcher, p3);

            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                headerContents.Text = String.Format("Count = {0}", count);
            });
            //Trace.WriteLine(string.Format("Data received on pipe number {0}:{1}", e.PipeNumber, stringBuffer.ToString()));
            last = DateTime.Now;
        }

        void OnConnected(object sender, EventArgs e)
        {
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                SolidColorBrush b = new SolidColorBrush();
                b.Color = Colors.LightGreen;
                recConnected.Fill = b;
                btnConnectDisconnect.Content = AppText.Disconnect;
                if (isPipeDiscoveryComplete)
                {

                }
                isConnected = true;
                //SetNetworkAvailableRectColor(netWorkAvailable);
                UpdateButtons();
            });
        }

        void OnDisconnected(object sender, ValueEventArgs<DisconnectReason> e)
        {
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                btnConnectDisconnect.Content = AppText.Connect;
                SetColorOfRect(false);
                isConnected = false;
                UpdateButtons();
            });
        }

        #endregion

        #region ui event handlers
        void OnBtnOpenCloseClick(object sender, RoutedEventArgs e)
        {
            int selectedItem = cboUsbSerial.SelectedIndex;
            string usbSerial;
            if (selectedItem >= 0)
            {
                usbSerial = (string)cboUsbSerial.Items[selectedItem];
            }
            else
            {
                MessageBox.Show(AppText.NoDeviceSelected);
                return;
            }

            try
            {
                this.Cursor = Cursors.Wait;
                if (!isOpen)
                {
                    OpenMasterEmulator(usbSerial);
                    btnOpenClose.IsEnabled = false;
                    if (!isRunning)
                    {
                        PerformPipeSetup();
                        Run();
                    }
                }
                else
                {
                    CloseMasterEmulator();
                }
            }
            catch (Exception ex)
            {
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    DisplayErrorMessage(ex);
                });
            }
            finally
            {
                this.Cursor = null;
            }
        }

        void OnBtnDeviceDiscoveryClick(object sender, RoutedEventArgs e)
        {
            IEnumerable<BtDevice> devices;
            try
            {
                this.Cursor = Cursors.Wait;
                devices = masterEmulator.DiscoverDevices();
                discoveredDevices.Clear();
                foreach (BtDevice dev in devices)
                {
                    string deviceName = "";
                    IDictionary<DeviceInfoType, string> deviceInfo = dev.DeviceInfo;
                    if (deviceInfo.ContainsKey(DeviceInfoType.CompleteLocalName))
                    {
                        deviceName = deviceInfo[DeviceInfoType.CompleteLocalName];
                    }
                    else if (deviceInfo.ContainsKey(DeviceInfoType.ShortenedLocalName))
                    {
                        deviceName = deviceInfo[DeviceInfoType.ShortenedLocalName];
                    }
                    else
                    {
                        deviceName = dev.DeviceAddress.Value;
                    }

                    StringValue val = new StringValue(deviceName, dev.DeviceAddress);
                    discoveredDevices.Add(val);
                }
            }
            catch (Exception ex)
            {
                DisplayErrorMessage(ex);
            }
            finally
            {
                this.Cursor = null;
            }

        }
        void OnBtnConnectDisconnectClick(object sender, RoutedEventArgs e)
        {
            if (!isConnected)
            {

                if (lbDeviceDiscovery.SelectedItems.Count > 0)
                {
                    StringValue selecterRow = (StringValue)lbDeviceDiscovery.SelectedItem;
                    BtDeviceAddress selectedDevice = (BtDeviceAddress)selecterRow.Data;
                    try
                    {
                        this.Cursor = Cursors.Wait;
                        BtDeviceAddress address = new BtDeviceAddress(selectedDevice.Value);
                        if (masterEmulator.Connect(address))
                        {
                            DiscoverPipes();
                            OpenRemotePipes();
                            isPipeDiscoveryComplete = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DisplayErrorMessage(ex);
                    }
                    finally
                    {
                        this.Cursor = null;
                    }
                }
            }
            else
            {
                try
                {
                    this.Cursor = Cursors.Wait;
                    masterEmulator.Disconnect();
                    isPipeDiscoveryComplete = false;
                }
                catch (Exception ex)
                {
                    DisplayErrorMessage(ex);
                }
                finally
                {
                    this.Cursor = null;
                }
            }
        }

        //void OnBtnToggleNwaClick(object sender, RoutedEventArgs e)
        //{
        //    netWorkAvailable = !netWorkAvailable;
        //    SendNetworkAvailability(netWorkAvailable);
        //    SetNetworkAvailableRectColor(netWorkAvailable);
        //}

        //void SetNetworkAvailableRectColor(bool available)
        //{
        //    SolidColorBrush brush = new SolidColorBrush();
        //    if (available)
        //    {
        //        brush.Color = Colors.LightGreen;
        //    }
        //    else
        //    {
        //        brush.Color = Colors.Red;
        //    }
        //    recNwa.Fill = brush;
        //}

        void SendNetworkAvailability(bool available)
        {
            byte[] data;
            if (available)
            {
                data = new byte[] { 0x01 };
            }
            else
            {
                data = new byte[] { 0x00 };
            }
            masterEmulator.SendData(networkAvailabilityPipe, data);
        }

        void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RunWorkerInitMasterEmulator();
            lbDeviceDiscovery.ItemsSource = discoveredDevices;
            SetColorOfRect(false);
            UpdateButtons();

            //for (int i = 0; i < animatedX.Length; i++)
            //{
            //    animatedX[i] = 2 * Math.PI * i / animatedX.Length;
            //    animatedY[i] = Math.Sin(animatedX[i]);
            //}
            //EnumerableDataSource<double> xSrc = new EnumerableDataSource<double>(animatedX);
            //xSrc.SetXMapping(x => x);
            //animatedDataSource = new EnumerableDataSource<double>(animatedY);
            //animatedDataSource.SetYMapping(y => y);

            //// Adding graph to plotter
            //plotter.AddLineGraph(new CompositeDataSource(xSrc, animatedDataSource),
            //    new Pen(Brushes.Magenta, 3),
            //    new PenDescription("Sin(x + phase)"));

            //timer.Interval = TimeSpan.FromMilliseconds(10);
            //timer.Tick += AnimatedPlot_Timer;
            //timer.IsEnabled = true;

            // Force evertyhing plotted to be visible
            //plotter.FitToView();

            // Create first source
            source1 = new ObservableDataSource<Point>();
            // Set identity mapping of point in collection to point on plot
            source1.SetXYMapping(p => p);

            // Create second source
            source2 = new ObservableDataSource<Point>();
            // Set identity mapping of point in collection to point on plot
            source2.SetXYMapping(p => p);

            // Create third source
            source3 = new ObservableDataSource<Point>();
            // Set identity mapping of point in collection to point on plot
            source3.SetXYMapping(p => p);

            // Add all three graphs. Colors are not specified and chosen random
            plotter.AddLineGraph(source1, Colors.Red, 2, "Data 1");
            plotter.AddLineGraph(source2, Colors.Green, 2, "Data 2");
            plotter.AddLineGraph(source3, Colors.Blue, 2, "Data 3");
        }

        void OnLbDeviceDiscoverySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtons();
        }

        #endregion

        void SetColorOfRect(bool connected)
        {
            if (connected)
            {
                SolidColorBrush b = new SolidColorBrush();
                b.Color = Colors.LightGreen;
                recConnected.Fill = b;
            }
            else
            {
                SolidColorBrush b = new SolidColorBrush();
                b.Color = Colors.Red;
                recConnected.Fill = b;
            }
        }

        void UpdateButtons()
        {
            btnDeviceDiscovery.IsEnabled = isOpen;


            if (lbDeviceDiscovery.SelectedItem != null)
            {
                btnConnectDisconnect.IsEnabled = true;
            }
            else
            {
                btnConnectDisconnect.IsEnabled = false;
            }
            //btnToggleNwa.IsEnabled = isConnected;
        }
    }
}
