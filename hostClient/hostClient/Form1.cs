using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace hostClient
{
    public partial class Form1 : Form
    {
        private TcpListener server;
        private Thread listenerThread;
        private bool isListening = false;
        private TcpClient client;
        private NetworkStream stream;
        private Thread receiveThread;

        public Form1()
        {
            InitializeComponent();

            // Setup mouse & keyboard event handlers for controlling client
            pictureBox1.MouseMove += PictureBox1_MouseMove;
            pictureBox1.MouseDown += PictureBox1_MouseDown;
            pictureBox1.MouseUp += PictureBox1_MouseUp;
            this.KeyDown += Form1_KeyDown;
            this.KeyUp += Form1_KeyUp;
            this.FormClosing += Form1_FormClosing;

            // Ensure form can receive key events
            this.KeyPreview = true;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            StartServer();
        }

        private void StartServer()
        {
            int port = 5000;
            server = new TcpListener(IPAddress.Any, port);//ip address
            server.Start();
            isListening = true;

            listenerThread = new Thread(ListenForClients);
            listenerThread.IsBackground = true;
            listenerThread.Start();

            Log($"[Host] Server started on port {port}");
        }

        private void ListenForClients()
        {
            while (isListening)
            {
                try
                {
                    client = server.AcceptTcpClient();
                    Log("[Host] Client connected.");

                    stream = client.GetStream();

                    // Read auth message length
                    byte[] lengthBuffer = new byte[4];
                    int lenRead = stream.Read(lengthBuffer, 0, 4);
                    if (lenRead != 4)
                    {
                        client.Close();
                        continue;
                    }
                    int authLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Read auth message
                    byte[] authBuffer = new byte[authLength];
                    int totalRead = 0;
                    while (totalRead < authLength)
                    {
                        int read = stream.Read(authBuffer, totalRead, authLength - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    string clientMessage = Encoding.UTF8.GetString(authBuffer);
                    Log($"[Host] Received: {clientMessage}");

                    string response = clientMessage == "HELLO_HOST" ? "ACCESS_GRANTED" : "ACCESS_DENIED";

                    // Send response length + response
                    byte[] respBytes = Encoding.UTF8.GetBytes(response);
                    byte[] respLen = BitConverter.GetBytes(respBytes.Length);
                    stream.Write(respLen, 0, 4);
                    stream.Write(respBytes, 0, respBytes.Length);
                    Log($"[Host] Sent: {response}");

                    if (response == "ACCESS_GRANTED")
                    {
                        // Start receiving screenshots & handle input
                        receiveThread = new Thread(ReceiveDataLoop);
                        receiveThread.IsBackground = true;
                        receiveThread.Start();
                    }
                    else
                    {
                        client.Close();
                    }
                }
                catch (Exception ex)
                {
                    Log("[Error] " + ex.Message);
                }
            }
        }

        private void ReceiveDataLoop()
        {
            try
            {
                while (isListening && client.Connected)
                {
                    // Read 4 bytes for next message type (for example)
                    // We will define a simple protocol:
                    // 1 = Screen image
                    // 2 = (reserved for future use)
                    // 3 = (reserved for future use)

                    // For now, just read screenshots continuously

                    // Read image length
                    byte[] lengthBuffer = new byte[4];
                    int read = stream.Read(lengthBuffer, 0, 4);
                    if (read != 4) break;

                    int imgLength = BitConverter.ToInt32(lengthBuffer, 0);
                    if (imgLength <= 0) break;

                    // Read image bytes
                    byte[] imgBytes = new byte[imgLength];
                    int totalRead = 0;
                    while (totalRead < imgLength)
                    {
                        int chunk = stream.Read(imgBytes, totalRead, imgLength - totalRead);
                        if (chunk == 0) break;
                        totalRead += chunk;
                    }

                    using (MemoryStream ms = new MemoryStream(imgBytes))
                    {
                        Image img = Image.FromStream(ms);
                        UpdatePictureBox(img);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[ReceiveDataLoop] Error: " + ex.Message);
            }
            finally
            {
                Log("[Host] Client disconnected.");
                client?.Close();
            }
        }

        // Mouse and Keyboard event handlers: send commands to client

        private void PictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            SendInputCommand($"MOUSE_MOVE|{e.X}|{e.Y}");
        }

        private void PictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            SendInputCommand($"MOUSE_DOWN|{e.Button}");
        }

        private void PictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            SendInputCommand($"MOUSE_UP|{e.Button}");
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            SendInputCommand($"KEY_DOWN|{e.KeyCode}");
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            SendInputCommand($"KEY_UP|{e.KeyCode}");
        }

        private void SendInputCommand(string command)
        {
            try
            {
                if (client == null || !client.Connected) return;
                byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
                byte[] lenBytes = BitConverter.GetBytes(cmdBytes.Length);
                stream.Write(lenBytes, 0, 4);
                stream.Write(cmdBytes, 0, cmdBytes.Length);
            }
            catch (Exception ex)
            {
                Log("[SendInputCommand] Error: " + ex.Message);
            }
        }

        private void UpdatePictureBox(Image img)
        {
            if (pictureBox1.InvokeRequired)
            {
                pictureBox1.Invoke(new Action<Image>(UpdatePictureBox), img);
            }
            else
            {
                pictureBox1.Image?.Dispose();
                pictureBox1.Image = new Bitmap(img);
            }
        }

        private void Log(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(Log), message);
            }
            else
            {
                Console.WriteLine(message);
                logBox.Items.Add(message);
                logBox.TopIndex = logBox.Items.Count - 1; // autoscroll
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            isListening = false;
            try
            {
                stream?.Close();
                client?.Close();
                listenerThread?.Abort();
                receiveThread?.Abort();
            }
            catch { }
            server?.Stop();
        }
    }
}
