using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Client
{
    public partial class Client : Form
    {
        private bool connected = false;
        private Thread client = null;
        private struct MyClient
        {
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private MyClient obj;
        private Task send = null;
        private bool exit = false;

        public Client()
        {
            InitializeComponent();
        }

        private void LogWrite(string msg = null)
        {
            if (!exit)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    if (msg == null)
                    {
                        logTextBox.Clear();
                    }
                    else
                    {
                        if (logTextBox.Text.Length > 0)
                        {
                            logTextBox.AppendText(Environment.NewLine);
                        }
                        logTextBox.AppendText(DateTime.Now.ToString("HH:mm") + " " + msg);
                    }
                });
            }
        }

        private void Connected(bool status)
        {
            if (!exit)
            {
                connected = status;
                connectButton.Invoke((MethodInvoker)delegate
                {
                    if (status)
                    {
                        connectButton.Text = "Disconnect";
                        LogWrite("[/ Client connected /]");
                    }
                    else
                    {
                        connectButton.Text = "Connect";
                        LogWrite("[/ Client disconnected /]");
                    }
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                bool dataAvailable = false;
                try
                {
                    dataAvailable = obj.stream.DataAvailable;
                }
                catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                if (dataAvailable)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                    }
                    catch (IOException e)
                    {
                        LogWrite(string.Format("[/ {0} /]", e.Message));
                        obj.handle.Set();
                    }
                    catch (ObjectDisposedException e)
                    {
                        LogWrite(string.Format("[/ {0} /]", e.Message));
                        obj.handle.Set();
                    }
                }
                else
                {
                    LogWrite(obj.data.ToString());
                    obj.data.Clear();
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void Connection(IPAddress localaddr, int port)
        {
            try
            {
                obj = new MyClient();
                obj.client = new TcpClient();
                obj.client.Connect(localaddr, port);
                obj.stream = obj.client.GetStream();
                obj.buffer = new byte[obj.client.ReceiveBufferSize];
                obj.data = new StringBuilder();
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                Connected(true);
                if (obj.stream.CanRead && obj.stream.CanWrite)
                {
                    while (obj.client.Connected)
                    {
                        try
                        {
                            obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                            obj.handle.WaitOne();
                        }
                        catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                        catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                    }
                }
                else
                {
                    LogWrite("[/ Stream cannot read/write /]");
                }
                obj.client.Close();
                Connected(false);
            }
            catch (SocketException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
        }

        private void ConnectButton_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                obj.client.Close();
            }
            else
            {
                if (client == null || !client.IsAlive)
                {
                    bool localaddrResult = IPAddress.TryParse(localaddrMaskedTextBox.Text, out IPAddress localaddr);
                    if (!localaddrResult)
                    {
                        LogWrite("[/ Address is not valid /]");
                    }
                    bool portResult = int.TryParse(portTextBox.Text, out int port);
                    if (!portResult)
                    {
                        LogWrite("[/ Port is not valid /]");
                    }
                    else if (port < 0 || port > 65535)
                    {
                        portResult = false;
                        LogWrite("[/ Port is out of range /]");
                    }
                    if (localaddrResult && portResult)
                    {
                        client = new Thread(() => Connection(localaddr, port))
                        {
                            IsBackground = true
                        };
                        client.Start();
                    }
                }
            }
        }

        private void Write(IAsyncResult result)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
            }
        }

        private void Send(string msg)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(msg);
                obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
            }
            catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
            catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
        }

        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (sendTextBox.Text.Length > 0)
                {
                    string msg = sendTextBox.Text;
                    sendTextBox.Clear();
                    LogWrite("<- You -> " + msg);
                    if (connected)
                    {
                        if (send == null || send.IsCompleted)
                        {
                            send = Task.Factory.StartNew(() => Send(msg));
                        }
                        else
                        {
                            send.ContinueWith(antecendent => Send(msg));
                        }
                    }
                }
            }
        }

        private void Client_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (connected)
            {
                exit = true;
                obj.client.Close();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            LogWrite();
        }
    }
}
