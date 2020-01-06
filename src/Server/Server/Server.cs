using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Server
{
    public partial class Server : Form
    {
        private bool active = false;
        private Thread listener = null;
        private long id = 0;
        private struct MyClient
        {
            public long id;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private ConcurrentDictionary<long, MyClient> list = new ConcurrentDictionary<long, MyClient>();
        private Task send = null;
        private Thread disconnect = null;
        private bool exit = false;

        public Server()
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

        private void Active(bool status)
        {
            if (!exit)
            {
                active = status;
                startButton.Invoke((MethodInvoker)delegate
                {
                    if (status)
                    {
                        startButton.Text = "Stop";
                        LogWrite("[/ Server started /]");
                    }
                    else
                    {
                        startButton.Text = "Start";
                        LogWrite("[/ Server stopped /]");
                    }
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
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
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
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
                    string msg = string.Format("<- Client {0} -> " + obj.data, obj.id);
                    LogWrite(msg);
                    if (send == null || send.IsCompleted)
                    {
                        send = Task.Factory.StartNew(() => Send(msg, obj.id));
                    }
                    else
                    {
                        send.ContinueWith(antecendent => Send(msg, obj.id));
                    }
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

        private void Connection(MyClient obj)
        {
            list.TryAdd(obj.id, obj);
            LogWrite(string.Format("[/ Client {0} connected /]", obj.id));
            if (obj.stream.CanRead && obj.stream.CanWrite)
            {
                while (obj.client.Connected)
                {
                    try
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), obj);
                        obj.handle.WaitOne();
                    }
                    catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                    catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                }
            }
            else
            {
                LogWrite(string.Format("[/ Client {0} stream cannot read/write /]", obj.id));
            }
            obj.client.Close();
            list.TryRemove(obj.id, out MyClient tmp);
            LogWrite(string.Format("[/ Client {0} connection closed /]", obj.id));
        }

        private void Listener(IPAddress localaddr, int port)
        {
            try
            {
                TcpListener listener = new TcpListener(localaddr, port);
                listener.Start();
                Active(true);
                while (active)
                {
                    if (listener.Pending())
                    {
                        MyClient obj = new MyClient();
                        obj.id = id;
                        obj.client = listener.AcceptTcpClient();
                        obj.stream = obj.client.GetStream();
                        obj.buffer = new byte[obj.client.ReceiveBufferSize];
                        obj.data = new StringBuilder();
                        obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                        Thread th = new Thread(() => Connection(obj))
                        {
                            IsBackground = true
                        };
                        th.Start();
                        id++;
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
                listener.Server.Close();
                Active(false);
            }
            catch (SocketException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (active)
            {
                active = false;
            }
            else
            {
                if (listener == null || !listener.IsAlive)
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
                        listener = new Thread(() => Listener(localaddr, port))
                        {
                            IsBackground = true
                        };
                        listener.Start();
                    }
                }
            }
        }

        private void Write(IAsyncResult result)
        {
            MyClient obj = (MyClient)result.AsyncState;
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

        private void Send(string msg, long id = -1)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            foreach (KeyValuePair<long, MyClient> obj in list)
            {
                if (id != obj.Value.id)
                {
                    try
                    {
                        obj.Value.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), obj.Value);
                    }
                    catch (IOException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                    catch (ObjectDisposedException e) { LogWrite(string.Format("[/ {0} /]", e.Message)); }
                }
            }
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
                    LogWrite("<- Server (You) -> " + msg);
                    if (send == null || send.IsCompleted)
                    {
                        send = Task.Factory.StartNew(() => Send("<- Server -> " + msg));
                    }
                    else
                    {
                        send.ContinueWith(antecendent => Send("<- Server -> " + msg));
                    }
                }
            }
        }

        private void Disconnect()
        {
            foreach (KeyValuePair<long, MyClient> obj in list)
            {
                obj.Value.client.Close();
            }
        }

        private void DisconnectButton_Click(object sender, EventArgs e)
        {
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() => Disconnect())
                {
                    IsBackground = true
                };
                disconnect.Start();
            }
        }

        private void Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            exit = true;
            active = false;
            if (disconnect == null || !disconnect.IsAlive)
            {
                disconnect = new Thread(() => Disconnect())
                {
                    IsBackground = true
                };
                disconnect.Start();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            LogWrite();
        }
    }
}
