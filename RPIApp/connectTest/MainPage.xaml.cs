using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Text;
using Microsoft.Azure.Devices.Client;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Devices.Spi;
using Windows.Devices.Gpio;
using Windows.Devices.I2c;
using System.Threading;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace connectTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public static MainPage Instance { get; private set; }

        private const string IotHubUri = "GreenHouse-IOT-HUB.azure-devices.net";
        private const string DeviceKey = "BNKN9mefkcawRIC6KhJy+MctdJ00OVgh3+DOtaOxOJM=";
        private static string DeviceId = "myFirstDevice"; //consider updating it to be the motitoredPlantID - it's unique
        private static int userID = getUserID();
        private static readonly Random Rand = new Random();
        private static DeviceClient _deviceClient;
        private static int monitoredPlantID = getMonitoredPlantID();

        private const int LED_PIN = 5;
        private static GpioPin pin;
        private static GpioPinValue pinValue;
        private SolidColorBrush redBrush = new SolidColorBrush(Windows.UI.Colors.Red);
        private SolidColorBrush grayBrush = new SolidColorBrush(Windows.UI.Colors.LightGray);

        



        // SENSORS
        private static SpiDevice _mcp3008;
        private static I2cDevice sensor;


        public MainPage()
        {
            this.InitializeComponent();

            Debug.WriteLine("Device Activated\n");
            _deviceClient = DeviceClient.Create(IotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey(DeviceId, DeviceKey), TransportType.Mqtt);

            Instance = this;
            initMCP3008();

            InitGPIO();

            InitI2C();

            SendDeviceToCloudMessagesAsync();

            ReceiveC2dAsync();
        }

        private async static void InitI2C()
        {
            string i2cDeviceSelector = I2cDevice.GetDeviceSelector();
            IReadOnlyList<DeviceInformation> devices = await DeviceInformation.FindAllAsync(i2cDeviceSelector);

            var sensor_settings = new I2cConnectionSettings(0x48);

            // This will result in a NullReferenceException in Timer_Tick below.
            sensor = await I2cDevice.FromIdAsync(devices[0].Id, sensor_settings);
        }

        private static void InitGPIO()
        {
            var gpio = GpioController.GetDefault();

            // Show an error if there is no GPIO controller
            if (gpio == null)
            {
                pin = null;
               
                return;
            }

            pin = gpio.OpenPin(LED_PIN);
            pinValue = GpioPinValue.Low;
            pin.Write(pinValue);
            pin.SetDriveMode(GpioPinDriveMode.Output);
        }

        private async static void initMCP3008()
        {
            //using SPI0 on the Pi
            var spiSettings = new SpiConnectionSettings(0);//for spi bus index 0
            spiSettings.ClockFrequency = 3600000; //3.6 MHz
            spiSettings.Mode = SpiMode.Mode0;

            string spiQuery = SpiDevice.GetDeviceSelector("SPI0");
            //using Windows.Devices.Enumeration;
            var deviceInfo = await DeviceInformation.FindAllAsync(spiQuery);
            if (deviceInfo != null && deviceInfo.Count > 0)
            {
                _mcp3008 = await SpiDevice.FromIdAsync(deviceInfo[0].Id, spiSettings);
                Debug.WriteLine("SPI is connected!!! :-");
            }
            else
            {
                Debug.WriteLine("SPI Device Not Found :-(");
            }
        }

        private static int getUserID()
        {
            return 1;
        }

       
        private static int getMonitoredPlantID()
        {
            return 5;
        }
        
        private static async void SendDeviceToCloudMessagesAsync()
        {
            while (true)
            {
                var currentTemperature = getCurrentTemperature();
                var currentHumidity = getCurrentHumidity();
               
                var telemetryDataPoint = new
                {
                    deviceId = DeviceId,
                    plantID = monitoredPlantID,
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                    userId = userID
                };
                var messageString = JsonConvert.SerializeObject(telemetryDataPoint);
                var message = new Message(Encoding.ASCII.GetBytes(messageString));
                message.Properties.Add("temperatureAlert", (currentTemperature > 30) ? "true" : "false");

                await _deviceClient.SendEventAsync(message);
                Debug.WriteLine("{0} > Sending message: {1}", DateTime.Now, messageString);

                await Task.Delay(5000);
            }
        }

        private static float getCurrentTemperature()
        {
            // Read data from I2C.
            var command = new byte[1];
            var temperatureData = new byte[2];


            // If this next line crashes with a NullReferenceException, then
            // there was a sharing violation on the device. (See StartScenarioAsync above.)
            //
            // If this next line crashes for some other reason, then there was
            // an error accessing the device.

            // Read temperature.
            command[0] = 0x48;
            // If this next line crashes, then there was an error accessing the sensor.
            float temperature = 10000;

            if (sensor!=null)
            {
                sensor.WriteRead(command, temperatureData);

                // Calculate and report the temperature.
                var rawTempReading = temperatureData[0] << 8 | temperatureData[1];
                var rawShifted = rawTempReading >> 4;
                temperature = rawShifted * 0.0625f;
                Instance.CurrentTemp.Text = temperature.ToString();

            }


            return temperature;
        }

        private static float getCurrentHumidity()
        {
            var transmitBuffer = new byte[3] { 1, 0x80, 0x00 };
            var receiveBuffer = new byte[3];
            int result=10000;

            if (_mcp3008 != null)
            {
                _mcp3008.TransferFullDuplex(transmitBuffer, receiveBuffer);
                result = ((receiveBuffer[1] & 3) << 8) + receiveBuffer[2];
                Instance.CurrentHumidity.Text = result.ToString();
            }
            

            return result;
        }

        private static async void ReceiveC2dAsync()
        {
            Debug.WriteLine("\nReceiving cloud to device messages from service");
            while (true)
            {
                Message receivedMessage = await _deviceClient.ReceiveAsync();
                if (receivedMessage == null) continue;

                Debug.WriteLine("Received message: {0}", Encoding.ASCII.GetString(receivedMessage.GetBytes()));
          
                await _deviceClient.CompleteAsync(receivedMessage);

                waterPlant();
            }
        }

        //Note: to change wattering logic so it finishes wattering a second after it starts and not on the  next activation of waterPalnt
        private static void waterPlant()
        {
            if (pin != null)
            {
                pinValue = GpioPinValue.High;
                pin.Write(pinValue);

                Task.Delay(3000).Wait();

                pinValue = GpioPinValue.Low;
                pin.Write(pinValue);
            }
            else
                InitGPIO();
        }

        private void ScenarioControls_SelectionChanged(object sender, RoutedEventArgs e)
        {

        }
    }
}
