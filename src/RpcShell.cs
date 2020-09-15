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
        /// <summary>
        ///  The current instance of the shell.
        /// </summary>
        /// <value>The shell.</value>
        public Shell Shell { get; } = new Shell();

        /// <summary>Gets or sets the current RpcShell singleton instance.</summary>
        /// <value>The instance.</value>
        public static RpcShell Instance { get; protected set; }


        /// <summary>Gets or sets the shell prompt.</summary>
        /// <value>The prompt.</value>
        public string Prompt
        {
            get => Shell.Prompt;
            set => Shell.Prompt = value;
        }


        /// <summary>  Create a Shell with the given target object which contains all marked methods</summary>
        /// <param name="target">The target can be any object which exposes public methods decorated with [Command].</param>
        public RpcShell(object target) : base(target)
        {
            Shell.Command += Dispatch;
            StackChanged += Shell.SetCurrentPath;
            ReadLine.AutoCompletionHandler = this;
            Instance = this;
            Inject.Add(typeof(Shell), Shell);
            Inject.Add(typeof(RpcShell), this);
        }

        /// <summary>
        /// Runs this instance with the target object.
        /// </summary>
        /// <returns>Exit code</returns>
        public int Run()
        {
            var exitCode = Shell.Run();
            if (Target is IDisposable disposable)
                disposable.Dispose();

            return exitCode;
        }

        /// <summary>
        /// Exits with the specified exit code.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public void Exit(int exitCode)
        {
            Shell.Exit(exitCode);
        }

        /// <summary>Shortcut methods which creates a new RpcShell and runs it</summary>
        /// <param name="target">The target.</param>
        /// <param name="prompt">The prompt.</param>
        /// <returns>Exit code</returns>
        public static int Run(object target, string prompt = Shell.DefaultPrompt)
        {
            var shell = new RpcShell(target) { Prompt = prompt };
            return shell.Run();
        }

        /// <summary>Shortcut methods which creates a new RpcShell and runs it</summary>
        /// <param name="target">The target.</param>
        /// <param name="prompt">The prompt.</param>
        /// <returns>Exit code</returns>
        public static int Run<T>(string prompt = Shell.DefaultPrompt) where T : new()
        {
            return Run(new T(), prompt);
        }

        #region Suggestions

        protected IEnumerable<string> GetAutoSuggestions(string text, int index)
        {
            if (!TryParse(text, out var partialArgs))
                return new string[] { };

            if (GetContext(partialArgs, out var name, out var args))
                if (GetMethod(name, out var method))
                {
                    var parameters = method.GetParameters();
                    var lastArg = args.LastOrDefault();
                    var secondLastArg = args.ElementAtOrDefault(args.Length - 2);
                    var ordinal = FindOrdinalLength(args);

                    if (IsFlag(lastArg))
                        return parameters.Where(p => !CanInject(Inject, p)).Select(p => $"-{p.Name}");

                    if (ordinal > parameters.Length)
                        return Enumerable.Empty<string>();

                    var selected =
                        IsFlag(secondLastArg) ?
                        FindParameterFor(secondLastArg, parameters)
                        :
                        parameters.ElementAtOrDefault(Math.Max(0, args.Length - 1));

                    if (selected == null)
                        return Enumerable.Empty<string>();

                    var suggestion = selected.GetCustomAttribute<SuggestAttribute>()?.MethodName ?? String.Empty;

                    try
                    {
                        var suggestionMethod = Target.GetType().GetMethod(suggestion);
                        if (suggestionMethod != null)
                        {
                            var suggestParams = new object[suggestionMethod.GetParameters().Length];
                            InjectArguments(suggestionMethod, new Dictionary<Type, object>
                            {
                                {typeof(string), lastArg },
                                {typeof(int), index },
                            }, ref suggestParams);

                            InjectArguments(suggestionMethod, Inject, ref suggestParams);

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
                        return selected.ParameterType.GetEnumNames();

                    if (selected.ParameterType == typeof(bool))
                        return new[] { "Y", "N", "true", "false" };

                    {
                        var history = 
                            Shell
                                .GetHistory()
                                .Where(item => item.StartsWith(name, StringComparison.InvariantCultureIgnoreCase))
                                .Select(item => { return (match: TryParse(item, out var itemargs), itemargs); })
                                .Where(expr => expr.match)
                                .Select(expr => expr.itemargs.ElementAtOrDefault(ordinal))
                                .Where(s => !String.IsNullOrEmpty(s));

                        return history;
                    }
                }

            return GetCommands();
        }

        protected int FindOrdinalLength(string[] args)
        {
            int ordinalLength = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (IsFlag(args[i]))
                {
                    i += 1;
                    continue;
                }

                ordinalLength += 1;
            }

            return ordinalLength;
        }

        char[] IAutoCompleteHandler.Separators { get => Shell.Separators; set => Shell.Separators = value; }

        string[] IAutoCompleteHandler.GetSuggestions(string text, int index)
        {
            return Shell.RankSuggestions(GetAutoSuggestions(text, index), text, index)
                        .Select(Shell.QuoteIfNeeded)
                        .ToArray();
        }
        #endregion

        #region Print Result

        /// <summary>
        ///   <para>
        ///  Default print action when returning a result.
        /// Usually methods return void.</para>
        /// </summary>
        /// <param name="result">The evaluation result.</param>
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

                    case IEnumerable<object> objects:
                        {
                            var enumerableType = objects.GetType();
                            var innerType =
                                enumerableType.HasElementType ?
                                enumerableType.GetElementType() :
                                enumerableType.GetGenericArguments().FirstOrDefault();

                            if (innerType != null)
                            {
                                var generator =
                                typeof(ConsoleTables.ConsoleTable)
                                    .GetMethod(nameof(ConsoleTables.ConsoleTable.From))
                                    .MakeGenericMethod(innerType);

                                var table = (ConsoleTables.ConsoleTable)generator.Invoke(null, new[] { result }) ;
                                table.Options.EnableCount = false;
                                table.Write();
                            }

                            return;
                        }

                    default:
                        Console.WriteLine(GenericToDataString.ObjectDumper.Dump(result));
                        return;
                }

            }
        }
        #endregion

        #region Invoke

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
                        PrintException(tie.InnerException);
                        return true;
                    }

                    if (exception is TaskCanceledException)
                        return true;

                    if (aggregateException.InnerExceptions.Count == 1)
                    {
                        //If there's only one, this must be that one
                        PrintException(exception);
                        return true;
                    }

                    return false;
                });
            }
            catch (System.Exception exception)
            {
                Error(exception.ToString());
            }
        }

        protected void PrintException(Exception exn)
        {
            if (exn == null)
                return;

            var exceptionType = exn.GetType().Name;
            var exceptionName = Shell.Humanize(exceptionType.Replace(nameof(Exception), string.Empty));
            if (string.IsNullOrEmpty(exceptionName))
                Error(exn.Message);
            else
                Error($"{exceptionName}: {exn.Message}");

            PrintException(exn.InnerException);
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
                var type = taskResult.GetType();

                if (type.FullName.Contains("VoidTaskResult"))
                    return null;

                return type.GetProperty(nameof(Task<object>.Result)).GetValue(taskResult);
            }

            return result;
        }

        #endregion

        #region Default Action

        /// <summary>Handle any command that didn't match</summary>
        /// <param name="name">The command name</param>
        /// <param name="args">Command arguments originally provided</param>
        protected override void DefaultActionHandler(string name, string[] args)
        {
            switch (name)
            {
                case "help":
                    {
                        Console.WriteLine(args.Length >= 1 ? GetHelp(args[0]) : GetHelp());
                        break;
                    }

                case "exit":
                    {
                        Shell.Exit(0);
                        break;
                    }

                default:
                    {
                        base.DefaultActionHandler(name, args);
                        var suggestions = Shell.RankSuggestions(GetCommands(), name, 0).ToArray();
                        if (suggestions.Any())
                            Error($"Did you mean: {String.Join(", ", suggestions)}");
                        break;
                    }
            }


        }
        #endregion
    }
}
