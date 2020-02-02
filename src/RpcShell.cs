using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Threading;

namespace NetShell
{
    public class RpcShell : RpcDispatcher, IAutoCompleteHandler
    {
        public Shell Shell { get; } = new Shell();

        [ThreadStatic]
        private static RpcShell _instance;

        public static RpcShell Instance
        {
            get { return _instance; }
            internal set { _instance = value; }
        }

        public string Prompt
        {
            get => Shell.Prompt;
            set => Shell.Prompt = value;
        }

        public RpcShell(object target) : base(target)
        {
            Shell.Command += Dispatch;
            StackChanged += Shell.SetCurrentPath;            
            ReadLine.AutoCompletionHandler = this;
            Instance = this;
            Inject.Add(typeof(Shell), Shell);
        }

        public int Run()
        {
            var exitCode = Shell.Run();
            if (Target is IDisposable disposable)
                disposable.Dispose();

            return exitCode;
        }

        public static int Run(object target, string prompt = Shell.DefaultPrompt)
        {
            var shell = new RpcShell(target) { Prompt = prompt };
            return shell.Run();
        }

        public static int Run<T>(string prompt = Shell.DefaultPrompt) where T: new()
        {
            return Run(new T(), prompt);
        }

        protected IEnumerable<string> GetAutoSuggestions(string text, int index)
        {
            if (!TryParse(text, out var partialArgs))
                return new string[] { };

            if (GetContext(partialArgs, out var name, out var args))
                if (GetMethod(name, out var method))
                {
                    var parameters = method.GetParameters();

                    if (args.Length > parameters.Length)
                        return new string[] { };

                    var selected = parameters[Math.Max(0, args.Length - 1)];

                    var suggestion = selected.GetCustomAttribute<SuggestAttribute>()?.MethodName ?? String.Empty;
                    
                    try
                    {
                        var suggestionMethod = Target.GetType().GetMethod(suggestion);
                        if (suggestionMethod != null)
                        {
                            var suggestParams = new object[] { args.Last(), index }.Take(suggestionMethod.GetParameters().Length).ToArray();

                            var result =
                                suggestionMethod.Invoke(Target, suggestParams) as IEnumerable<string>;
                            if (result != null)
                                return result;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Trace.TraceError(ex.ToString());
                    }

                    if (selected.ParameterType.IsEnum)
                        return selected.ParameterType.GetEnumNames().Select(s => s.ToLower());
                }

            return GetCommands();
        }

        char[] IAutoCompleteHandler.Separators { get => Shell.Separators; set => Shell.Separators = value; }

        string[] IAutoCompleteHandler.GetSuggestions(string text, int index)
        {
            return Shell.RankSuggestions(GetAutoSuggestions(text, index), text, index)
                        .Select(Shell.QuoteIfNeeded)
                        .ToArray();
        }

        protected override void OnResult(object result)
        {
            base.OnResult(result);

            if (result != null)
            {
                switch (result)
                {
                    case string str:
                        Console.WriteLine(str);
                        return;

                    case IEnumerable<string> strings:
                        {
                            var arr = strings.ToArray();
                            Console.WriteLine(String.Join(arr.Length < 10 ? "\t" : "\n", strings));
                            return;
                        }

                    default:
                        Console.WriteLine(GenericToDataString.ObjectDumper.Dump(result));
                        return;
                }

            }
        }

        protected override void Invoke(MethodInfo methodInfo, object[] typedArgs, Dictionary<Type, object> injectable)
        {
            var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;

            injectable[typeof(CancellationToken)] = token;
            InjectArguments(methodInfo, injectable, ref typedArgs);
            
            try
            {
                var task = InvokeAsync(methodInfo, typedArgs);

                using (Shell.CancelOnKey(cancellation, 200))
                {
                    task.Wait(token);
                }

                if (task.IsCompleted)
                    OnResult(task.Result);
            }
            catch (OperationCanceledException)
            {
                Error("Command canceled");
            }
            catch (AggregateException aggregateException)
            {
                aggregateException.Handle(exception =>
                {
                    if (exception is TargetInvocationException tie)
                    {
                        var exceptionType = tie.InnerException.GetType().Name;
                        var exceptionName = Shell.Humanize(exceptionType.Replace(nameof(Exception), string.Empty));
                        Error($"{exceptionName}: {tie.InnerException.Message}");
                        return true;
                    }

                    if (exception is TaskCanceledException)
                        return true;

                    return false;
                });
            }
            catch(System.Exception exception)
            {
                Error(exception.ToString());
            }            
        }


        protected async Task<object> InvokeAsync(MethodInfo methodInfo, object[] typedArgs)
        {
            await Task.Yield();

            Instance = this;

            //Check Thread usage in async
            //Console.WriteLine(">>Thread " + Thread.CurrentThread.ManagedThreadId);

            var result = methodInfo.Invoke(Target, typedArgs);
            if (result is Task taskResult)
            {
                await taskResult.ConfigureAwait(false);
                if (taskResult.GetType().FullName.Contains("VoidTaskResult"))
                    return null;

                return ((dynamic)taskResult).Result;
            }

            return result;
        }

        protected override void OnCommandNotFound(string name)
        {
            base.OnCommandNotFound(name);
            Error("Did you mean: " + String.Join(", ", Shell.RankSuggestions(GetCommands(), name, 0)));
        }
    }
}
