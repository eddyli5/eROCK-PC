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

using Nordicsemi;

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
        bool isPipeDiscoveryComplete = false;
        bool isOpen = false;
        bool isConnected = false;
        bool isRunning = false;
        //bool netWorkAvailable = false;
        int networkAvailabilityPipe;
        //int networkAvailabilityReqPipe;

        public MainWindow()
        {
            InitializeComponent();
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
            tbTest.Text = s;
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
            foreach (byte element in e.PipeData)
            {
                stringBuffer.AppendFormat(" 0x{0:X2}", element);
            }

            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                ShowInTextbox(stringBuffer.ToString());
            });
            Trace.WriteLine(string.Format("Data received on pipe number {0}:{1}", e.PipeNumber, stringBuffer.ToString()));
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
