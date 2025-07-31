using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace clientHost
{
    public partial class Form1 : Form
    {
        private TcpClient client;
        private NetworkStream stream;
        private Thread sendThread;
        private Thread receiveThread;
        private bool running = false;

        public Form1()
        {
            InitializeComponent();
            this.FormClosing += Form1_FormClosing;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ConnectToHost();
        }

        private void ConnectToHost()
        {
            string hostIp = "***.***.***.***"; // 🔧 Replace with Host IP
            int port = 5000;

            try
            {
                client = new TcpClient();
                client.Connect(hostIp, port);
                stream = client.GetStream();

                // Send AUTH message with length prefix
                string authMsg = "HELLO_HOST";
                byte[] authBytes = Encoding.UTF8.GetBytes(authMsg);
                byte[] authLen = BitConverter.GetBytes(authBytes.Length);
                stream.Write(authLen, 0, 4);
                stream.Write(authBytes, 0, authBytes.Length);

                // Read response length and response
                byte[] respLenBuf = new byte[4];
                int read = stream.Read(respLenBuf, 0, 4);
                if (read != 4)
                {
                    Log("Failed to read response length");
                    return;
                }

                int respLen = BitConverter.ToInt32(respLenBuf, 0);
                byte[] respBuf = new byte[respLen];
                int totalRead = 0;
                while (totalRead < respLen)
                {
                    int r = stream.Read(respBuf, totalRead, respLen - totalRead);
                    if (r == 0) break;
                    totalRead += r;
                }
                string response = Encoding.UTF8.GetString(respBuf);
                Log("[Client] Server Response: " + response);

                if (response == "ACCESS_GRANTED")
                {
                    MessageBox.Show("Access Granted by Host!", "Connection Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    running = true;
                    sendThread = new Thread(SendScreenLoop);
                    sendThread.IsBackground = true;
                    sendThread.Start();

                    receiveThread = new Thread(ReceiveInputCommands);
                    receiveThread.IsBackground = true;
                    receiveThread.Start();
                }
                else
                {
                    MessageBox.Show("Access Denied by Host.", "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Cleanup();
                }
            }
            catch (Exception ex)
            {
                Log("[Client] Error: " + ex.Message);
                MessageBox.Show("Connection Error:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Cleanup();
            }
        }

        private void SendScreenLoop()
        {
            try
            {
                while (running && client.Connected)
                {
                    using (Bitmap bmp = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                        }

                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Jpeg); // compress screen image to jpeg
                            byte[] imgBytes = ms.ToArray();

                            // Send image length prefix
                            byte[] lenBytes = BitConverter.GetBytes(imgBytes.Length);
                            stream.Write(lenBytes, 0, 4);

                            // Send image bytes
                            stream.Write(imgBytes, 0, imgBytes.Length);
                            stream.Flush();
                        }
                    }
                    Thread.Sleep(200); // Adjust for desired FPS
                }
            }
            catch (Exception ex)
            {
                Log("[SendScreenLoop] Error: " + ex.Message);
                running = false;
            }
        }

        private void ReceiveInputCommands()
        {
            try
            {
                while (running && client.Connected)
                {
                    // Read length of incoming command
                    byte[] lenBuf = new byte[4];
                    int read = stream.Read(lenBuf, 0, 4);
                    if (read != 4) break;

                    int cmdLen = BitConverter.ToInt32(lenBuf, 0);
                    if (cmdLen <= 0) break;

                    byte[] cmdBuf = new byte[cmdLen];
                    int totalRead = 0;
                    while (totalRead < cmdLen)
                    {
                        int r = stream.Read(cmdBuf, totalRead, cmdLen - totalRead);
                        if (r == 0) break;
                        totalRead += r;
                    }
                    string command = Encoding.UTF8.GetString(cmdBuf);

                    ProcessInputCommand(command);
                }
            }
            catch (Exception ex)
            {
                Log("[ReceiveInputCommands] Error: " + ex.Message);
                running = false;
            }
            finally
            {
                Cleanup();
            }
        }

        private void ProcessInputCommand(string command)
        {
            // Commands format:
            // MOUSE_MOVE|x|y
            // MOUSE_DOWN|Left/Right/Middle
            // MOUSE_UP|Left/Right/Middle
            // KEY_DOWN|KeyCode
            // KEY_UP|KeyCode

            string[] parts = command.Split('|');
            if (parts.Length == 0) return;

            try
            {
                switch (parts[0])
                {
                    case "MOUSE_MOVE":
                        if (parts.Length == 3 &&
                            int.TryParse(parts[1], out int x) &&
                            int.TryParse(parts[2], out int y))
                        {
                            SetCursorPos(x, y);
                        }
                        break;

                    case "MOUSE_DOWN":
                        if (parts.Length == 2)
                        {
                            MouseEvent(parts[1], true);
                        }
                        break;

                    case "MOUSE_UP":
                        if (parts.Length == 2)
                        {
                            MouseEvent(parts[1], false);
                        }
                        break;

                    case "KEY_DOWN":
                        if (parts.Length == 2 && Enum.TryParse(parts[1], out Keys keyDown))
                        {
                           // keybd_event((byte)keyDown, 0, 0, 0);
                            keybd_event((byte)keyDown, 0, 0, UIntPtr.Zero);
                        }
                        break;

                    case "KEY_UP":
                        if (parts.Length == 2 && Enum.TryParse(parts[1], out Keys keyUp))
                        {
                            //keybd_event((byte)keyUp, 0, KEYEVENTF_KEYUP, 0);
                            keybd_event((byte)keyUp, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("[ProcessInputCommand] Error: " + ex.Message);
            }
        }

        private void MouseEvent(string button, bool down)
        {
            uint dwFlags = 0;

            switch (button.ToLower())
            {
                case "left":
                    dwFlags = down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP;
                    break;
                case "right":
                    dwFlags = down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP;
                    break;
                case "middle":
                    dwFlags = down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP;
                    break;
            }

            mouse_event(dwFlags, 0, 0, 0, UIntPtr.Zero);
        }

        private void Cleanup()
        {
            running = false;
            stream?.Close();
            client?.Close();
            sendThread?.Abort();
            receiveThread?.Abort();
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            
        }
        private void Log(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(Log), message);
            }
            else
            {
                Console.WriteLine(message);
                textBox1.Text = message;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Cleanup();
        }

        // WinAPI functions for input simulation:

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);


        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint KEYEVENTF_KEYUP = 0x0002;
    }
}
