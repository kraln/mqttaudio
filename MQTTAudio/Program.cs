using System;
using System.Net;
using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;
using NAudio;
using NAudio.Wave;
using System.Reflection.Metadata;

namespace MQTTAudio
{
    class Program
    {
        static string MQTT_BROKER_ADDRESS = "localhost";
        static MqttClient client;

        static void Main(string[] args)
        {
            if(args.Length != 1)
            {
                Console.WriteLine("Usage: mqttaudio (sender|reciever)");
                Environment.Exit(1);
            }

            switch(args[0])
            {
                case "sender":
                    Console.WriteLine("Starting sender...");
                    StartPublisher();
                    break;

                case "reciever":
                    Console.WriteLine("Starting reciever...");
                    StartSubscriber();
                    break;

                default:
                    Console.WriteLine(args[0]);
                    break;
            }
        }
        static void StartPublisher()
        {
            // create client instance 
            client = new MqttClient(MQTT_BROKER_ADDRESS);

            // connect to broker with random GUID
            string clientId = Guid.NewGuid().ToString();
            client.Connect(clientId);

            Console.WriteLine("Connecting...");

            // get microphone data in (can read from a file later ... )
            WaveInCapabilities devInfo = WaveIn.GetCapabilities(0);
            WaveInEvent waveSource = new WaveInEvent();
            waveSource.DeviceNumber = 0;
            waveSource.WaveFormat = new WaveFormat(16000, 1); // 16khz mono
            waveSource.BufferMilliseconds = 50; // default 100, but now we will get 50ms chunks of about 1600 bytes
            waveSource.StartRecording();
            waveSource.DataAvailable += new EventHandler<WaveInEventArgs>(waveInStream_DataAvailable);

            Console.WriteLine($"Sending microphone data 16khz mono { waveSource.WaveFormat.Encoding } { waveSource.WaveFormat.BitsPerSample }bit ");

        }

        static void waveInStream_DataAvailable(object sender, WaveInEventArgs e)
        {
            client.Publish("/audio/stream", e.Buffer, MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE, false);
            Console.WriteLine($"{DateTime.UtcNow} Sent {e.BytesRecorded} bytes");
        }

        static BufferedWaveProvider sourceWaveData;

        static void StartSubscriber()
        {
            // create client instance 
            client = new MqttClient(MQTT_BROKER_ADDRESS);

            /* Setup audio buffer */
            sourceWaveData = new BufferedWaveProvider(new WaveFormat(16000, 1));
            sourceWaveData.BufferDuration = System.TimeSpan.FromMilliseconds(1000); // 20 "blocks"; 32kb

            // register to message received 
            client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;

            string clientId = Guid.NewGuid().ToString();
            client.Connect(clientId);

            client.Subscribe(
                new string[] { "/audio/stream" },
                new byte[] { MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE }
            );

            Console.WriteLine("Listening for data...");

            WasapiOut player = new WasapiOut();
            //WaveOut player = new WaveOut();
            player.Init(sourceWaveData);

            // while buffer is less than 200ms
            // wait for the buffer to grow 
            // otherwise
            // play buffer

            for (;;)
            { 
                
                if (sourceWaveData.BufferedDuration < System.TimeSpan.FromMilliseconds(25))
                {
                    if (player.PlaybackState == PlaybackState.Playing)
                    {
                        Console.WriteLine("Buffer underrun // no data, pausing playback...");
                        player.Pause();
                    }
                }
            
                if(player.PlaybackState != PlaybackState.Playing && sourceWaveData.BufferedDuration > System.TimeSpan.FromMilliseconds(200))
                {
                    player.Play();
                    Console.WriteLine("(re-)starting playback...");
                }
            }

        }
        static void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            // handle message received 
            try
            {
                sourceWaveData.AddSamples(e.Message, 0, e.Message.Length);
                Console.WriteLine($"Got { e.Message.Length } bytes via MQTT.");
            } 
            catch(Exception ex)
            {
                Console.WriteLine("Buffer overflow!");
            }
               
        }
    }
}
