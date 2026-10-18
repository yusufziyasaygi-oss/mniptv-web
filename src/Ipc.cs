using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace MNPlayer;

/// Tiny one-way named-pipe messaging between MN Player processes of the same user.
internal static class Ipc
{
    public static string MainPipe => "MNPlayer.Main." + Environment.UserName;
    public static string PreviewPipe => "MNPlayer.Preview." + Environment.UserName;

    public static bool Send(string pipe, string message, int timeoutMs = 400)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
            client.Connect(timeoutMs);
            var bytes = Encoding.UTF8.GetBytes(message);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Serve(string pipe, Action<string> handler)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var text = reader.ReadToEnd();
                    if (text.Length > 0) handler(text);
                }
                catch (Exception)
                {
                    Thread.Sleep(200);
                }
            }
        }) { IsBackground = true, Name = "MNPlayer-IPC" };
        thread.Start();
    }
}
