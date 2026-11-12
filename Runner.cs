namespace ToolManager;

using System.IO.Pipes;

internal static class Runner
{
    public static async Task<int> MainAsync(string requestPath, string pipeName)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(15000);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        using var gate = new SemaphoreSlim(1);
        async Task Send(RunnerMessage message)
        {
            await gate.WaitAsync();
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(message));
            }
            finally
            {
                gate.Release();
            }
        }

        try
        {
            var command = reader.ReadLineAsync();
            var request = JsonSerializer.Deserialize<RunRequest>(await File.ReadAllTextAsync(requestPath), Store.Json)!;
            Store.Root = request.Root;
            using var session = new ConsoleSession(request);
            await Send(new("started"));
            async Task Pump(StreamReader source, string kind)
            {
                var buffer = new char[2048];
                int count;
                try
                {
                    while ((count = await source.ReadAsync(buffer)) > 0)
                    {
                        await Send(new(kind, new string(buffer, 0, count)));
                    }
                }
                catch (IOException e) when ((e.HResult & 0xffff) is 109 or 232) { /* Windows pipe EOF. */ }
            }

            var output = Task.Run(() => Pump(session.Output, "stdout"));
            var errors = Task.Run(() => Pump(session.Error, "stderr"));
            bool stopping = false, forced = false;
            Task input = Task.CompletedTask;
            DateTime deadline = DateTime.MaxValue;
            while (session.Active)
            {
                if (command.IsCompleted)
                {
                    var line = await command;
                    var message = line is null ? new RunnerMessage("stop") : JsonSerializer.Deserialize<RunnerMessage>(line)!;
                    if (line is not null)
                    {
                        command = reader.ReadLineAsync();
                    }

                    if (message.Kind == "stop" && !stopping)
                    {
                        stopping = true;
                        deadline = DateTime.UtcNow.AddSeconds(request.Timeout);
                        try
                        {
                            session.Interrupt();
                            await Send(new("stopping", request.Timeout.ToString()));
                        }
                        catch (Exception e)
                        {
                            await Send(new("notice", "Ctrl+C delivery failed: " + e.Message));
                        }
                    }
                    else if (message.Kind == "input" && !stopping)
                    {
                        if (session.Input is null)
                        {
                            await Send(new("notice", "Input is not enabled for this run."));
                        }
                        else if (message.Text.Length > RunRequest.MaximumInputCharacters || !input.IsCompleted)
                        {
                            await Send(new("notice", "Input rejected: line is too long or previous input is still pending."));
                        }
                        else
                        {
                            input = Task.Run(async () =>
                        {
                            try
                            {
                                await session.Input.WriteLineAsync(message.Text);
                            }
                            catch (IOException)
                            {
                                if (session.Active)
                                {
                                    await Send(new("notice", "The tool closed its input stream."));
                                }
                            }
                        });
                        }
                    }
                }

                if (stopping && DateTime.UtcNow >= deadline)
                {
                    session.Kill();
                    forced = true;
                    break;
                }

                await Task.Delay(50);
            }

            await Task.WhenAll(output, errors, input);
            await Send(new("exit", forced ? "Force-stopped" : stopping ? "Stopped" : "Finished", session.ExitCode));
            return 0;
        }
        catch (Exception e)
        {
            try
            {
                await Send(new("error", e.Message));
            }
            catch (IOException) { }
            return 1;
        }
    }
}
