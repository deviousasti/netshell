using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NetShell
{
    /// <summary>
    ///  Mark a method as a command
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class CommandAttribute : Attribute
    {

        /// <summary>
        /// Sets the name of the command, prefer lowercase names
        /// separated by hyphens
        /// </summary>
        /// <value>The name of the command.</value>
        public string CommandName { get; }

        /// <summary>
        /// Sets the text which will be displayed as help.
        /// </summary>
        /// <value>
        /// The help text.
        /// </value>
        public string HelpText { get; set; }

        /// <summary>
        /// Marks this method as a command
        /// </summary>
        public CommandAttribute() { }

        /// <summary>
        /// Marks this method as a command
        /// </summary>
        /// <param name="commandName">Set the command name</param>
        public CommandAttribute(string commandName)
        {
            CommandName = commandName;
        }

        /// <summary>
        /// Marks this method as a command
        /// </summary>
        /// <param name="commandName">Set the command name</param>
        /// <param name="helpText">Set the help text</param>
        public CommandAttribute(string commandName, string helpText)
        {
            CommandName = commandName;
            HelpText = helpText;
        }
    }

    /// <summary>
    /// Mark this method as the default command handler, run when no
    /// other matching commands have been found
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class DefaultCommandAttribute : Attribute
    {
    }

    /// <summary>
    /// Names the method used for picking suggestions
    /// </summary>
    /// <seealso cref="System.Attribute" />
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class SuggestAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the method.
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// Name the method used which returns a list of suggestions
        /// </summary>
        /// <param name="methodName">Name of the method.</param>
        public SuggestAttribute(string methodName)
        {
            this.MethodName = methodName;
        }
    }

    /// <summary>
    /// Represents the normal form of a command dispatch
    /// </summary>
    /// <param name="method">The method name .</param>
    /// <param name="args">The method arguments.</param>
    public delegate void CommandDelegate(string method, string[] args);

    /// <summary>
    /// This is the underlying class responsible 
    /// for translating text commands to method calls
    /// </summary>
    public class RpcDispatcher
    {
        #region Properties
        /// <summary>
        /// Gets or sets the stack push command.
        /// When a command line ends with this string, it causes the
        /// existing arguments to be pushed to stack
        /// </summary>
        /// <value>
        /// The stack push command. Default is '\'
        /// </value>
        public string StackPushCommand { get; set; } = "\\";

        /// <summary>
        /// Gets or sets the stack pop command.
        /// When a command line equals this string, it causes the
        /// existing arguments to be popped one at a time
        /// </summary>
        /// <value>
        /// The stack push command. Default is '..'
        /// </value>
        public string StackPopCommand { get; set; } = "..";

        /// <summary>
        /// Gets or sets the stack clear command.
        /// When a command line equals this string, it causes the
        /// existing argument stack to be cleared
        /// </summary>
        /// <value>
        /// The stack clear command. Default is '...'
        /// </value>
        public string StackClearCommand { get; set; } = "...";

        /// <summary>
        /// Gets the target of the RPC calls.
        /// </summary>
        public object Target { get; }


        /// <summary>
        /// Gets or sets the error handler function.
        /// </summary>
        public Action<string> Error { get; set; } = text => ConsoleUtil.Error(text);

        /// <summary>
        /// Gets the argument stack.
        /// </summary>
        public Stack<string> Stack { get; }

        /// <summary>
        /// Gets or sets the default action handler - called when no matching method has been found.
        /// </summary>
        public CommandDelegate DefaultAction { get; set; }

        /// <summary>
        /// Occurs when the argument stack has been changed.
        /// </summary>
        public event Action<IEnumerable<string>> StackChanged;

        /// <summary>
        /// Mapping of injectable types
        /// </summary>
        protected Dictionary<Type, Object> Inject { get; }

        /// <summary>
        /// Mapping of names to methods
        /// </summary>
        private Dictionary<string, MethodInfo> LookupTable { get; }

        /// <summary>
        /// Enumerates the available commands.
        /// </summary>
        public IEnumerable<string> GetCommands() =>
            this.LookupTable.Keys.Select(k => k.ToLowerInvariant());

        /// <summary>
        /// Gets the method corresponding to the name (case insensitive).
        /// </summary>
        /// <returns>true if found</returns>
        protected bool GetMethod(string name, out MethodInfo methodInfo)
        {
            return LookupTable.TryGetValue((name ?? string.Empty).ToUpperInvariant(), out methodInfo);
        }

        #endregion

        #region Ctor
        /// <summary>
        /// Initializes a new instance of the <see cref="RpcDispatcher"/> class.
        /// </summary>
        /// <param name="target">The target object containing the RPC methods.</param>
        public RpcDispatcher(object target)
        {
            Target = target;
            Stack = new Stack<string>();
            Inject = new Dictionary<Type, object>() {
                { typeof(RpcDispatcher), this },
                { typeof(CancellationToken), default }
            };

            var methods = target.GetType().GetMethods();
            var attributes = methods
                .SelectMany(method => method.GetCustomAttributes<CommandAttribute>().Select(attr => (method, attr)));
            var defaultCommand = methods.FirstOrDefault(m => m.GetCustomAttributes<DefaultCommandAttribute>().Any());

            try
            {
                LookupTable = attributes.ToDictionary(pair => (pair.attr.CommandName ?? pair.method.Name).ToUpperInvariant(), pair => pair.method);

                DefaultAction = defaultCommand != null ?
                    (name, args) => Invoke(defaultCommand, new object[] { name, args }, Inject)
                    :
                    new CommandDelegate(DefaultActionHandler);
            }
            catch (ArgumentException)
            {
                var duplicate = attributes.GroupBy(a => a.attr.CommandName).Where(g => g.Count() > 1).FirstOrDefault();
                if (duplicate == null)
                    throw;

                throw new ArgumentException(
                    $"There is a duplicate definition for the command '{duplicate.Key}'" +
                    $" on methods {String.Join(" and ", duplicate.Select(d => d.method.Name))}"
                    );
            }
        }
        #endregion

        #region Parse
        /// <summary>
        /// Try to parse the text into a list of arguments
        /// </summary>
        public bool TryParse(string text, out string[] args) => TryParse(new StringReader(text), out args);

        /// <summary>
        /// Try to parse the text into a list of arguments
        /// </summary>
        public bool TryParse(TextReader reader, out string[] args)
        {
            try
            {
                using (var parser =
                    new TextFieldParser(reader)
                    {
                        Delimiters = new[] { " " },
                        CommentTokens = new[] { "#" },
                        TrimWhiteSpace = true,
                        ShouldQuoteEnclosedFields = true
                    })
                {
                    args = parser.ReadFields()?.SkipWhile(String.IsNullOrWhiteSpace).ToArray();
                    if (args == null || args.Length == 0)
                    {
                        args = new string[] { };
                        return true;
                    }

                    return true;
                }
            }
            catch (MalformedLineException) //This is always going to be line 1
            {
                Error($"Invalid syntax or unclosed quotes in line");
            }

            args = null;
            return false;
        }

        /// <summary>
        /// Determines whether the specified argument is a flag.
        /// </summary>
        protected bool IsFlag(string arg) => arg != null && arg.StartsWith("-");

        /// <summary>
        /// Finds the parameter for the corresponding switch.
        /// </summary>
        protected ParameterInfo FindParameterFor(string flagName, ParameterInfo[] parameters)
        {
            var name = flagName.Replace('-', '\0');
            return Array.Find(parameters, p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }

        #endregion

        #region Dispatch

        /// <summary>
        /// Dispatches an RPC call with the specified command text
        /// </summary>        
        public bool Dispatch(string input) => Dispatch(new StringReader(input));

        /// <summary>
        /// Dispatches an RPC call with the specified input reader
        /// </summary>
        public bool Dispatch(TextReader reader)
        {
            if (!TryParse(reader, out var args))
                return false;

            return Dispatch(args);
        }

        /// <summary>
        /// Dispatches an RPC call with the parsed set of arguments
        /// </summary>
        public bool Dispatch(IEnumerable<string> arguments)
        {
            if (!GetContext(arguments, out var name, out var rawargs))
                return false;

            var args = rawargs.Where(s => !String.IsNullOrWhiteSpace(s)).ToArray();

            var last = arguments.Count() == 1 ? arguments.Single() : String.Empty;

            if (rawargs.LastOrDefault() == StackPushCommand)
            {
                arguments.TakeWhile(a => a != StackPushCommand).ToList().ForEach(Stack.Push);
                return OnStackChanged();
            }

            if (last == StackPopCommand)
            {
                if (Stack.Count > 0)
                    Stack.Pop();

                return OnStackChanged();
            }

            if (last == StackClearCommand)
            {
                Stack.Clear();
                return OnStackChanged();
            }

            if (!GetMethod(name, out var methodInfo))
            {
                OnCommandNotFound(name, args);
                return false;
            }

            var injectable = Inject;
            var parameters = methodInfo.GetParameters();
            object[] typedArgs;

            try
            {
                HashSet<ParameterInfo> usedParams = new HashSet<ParameterInfo>();
                typedArgs = parameters.Select(GetDefaultValue).ToArray();

                for (int i = 0, param_order = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var next = i + 1 < args.Length ? args[i + 1] : null;
                    var ordered = parameters[param_order];

                    try
                    {
                        if (IsFlag(arg))
                        {
                            var position = Array.IndexOf(parameters, FindParameterFor(arg, parameters));
                            if (position == -1)
                            {
                                Error($"No parameter named {arg} was found. Valid parameters: {String.Join(", ", parameters.Select(p => p.Name))}");
                                return false;
                            }

                            ordered = parameters[position];

                            if (!usedParams.Add(ordered))
                            {
                                Error($"Parameter {arg} was specified more than once");
                                return false;
                            }

                            if (IsFlag(next) || next == null)
                            {
                                if (ordered.ParameterType != typeof(bool))
                                {
                                    Error($"A value was not specified for -{ordered.Name}");
                                    return false;
                                }

                                //attempted to use as a switch, rather than 
                                //a parameter with a value
                                typedArgs[position] = ConvertArg("true", ordered);
                                continue;
                            }

                            //consider next argument taken
                            typedArgs[position] = ConvertArg(next, ordered);

                            i += 1; //skip ahead
                            continue;
                        }

                        if (!usedParams.Add(ordered))
                        {
                            Error($"The parameter for {arg} was already specified at position ({param_order})");
                            return false;
                        }

                        //regular parameter order
                        typedArgs[param_order] = ConvertArg(arg, ordered);
                        param_order += 1;

                    }
                    catch (Exception)
                    {
                        if (IsFlag(arg))
                            Error($"Could not convert value for {arg} from '{next}'");
                        else
                            Error($"Could not convert value for -{ordered.Name} from '{arg}'");

                        if (ordered.ParameterType.IsEnum)
                        {
                            Error($"Valid values for {ordered.Name}: {String.Join(", ", Enum.GetNames(ordered.ParameterType))}");
                        }
                        else
                        {
                            Error($"Expected {GetParamInfo(ordered)}");
                        }

                        Error(GetSyntax(name));

                        return false;
                    }
                }

                var requiredParameters = parameters.Where(p => !p.HasDefaultValue && !CanInject(injectable, p));
                if (!requiredParameters.All(usedParams.Contains))
                {
                    Error($"Incorrect number of arguments. Missing: {String.Join(",", requiredParameters.Where(p => !usedParams.Contains(p)).Select(p => p.Name))}");
                    Error(GetSyntax(name));
                    return false;
                }

            }
            catch (System.Exception exception)
            {
                Error("Invalid arguments: " + exception.Message);
                Error(GetSyntax(name));

                return false;
            }


            try
            {
                Invoke(methodInfo, typedArgs, injectable);
            }
            catch (System.Exception exception)
            {
                Error(exception.ToString());
            }

            return true;
        }

        #endregion

        #region Injection

        /// <summary>
        /// Registers the specified type and instance for dependency injection.
        /// </summary>
        public void Register(Type type, object obj) => Inject[type] = obj;

        /// <summary>
        /// Registers the specified type for dependency injection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj">The instance.</param>
        public void Register<T>(T obj) => Register(typeof(T), obj);

        /// <summary>
        /// Gets the instance of type T.
        /// </summary>
        public T GetInstance<T>() => (T)Inject[typeof(T)];

        protected bool CanInject(Dictionary<Type, object> injectable, ParameterInfo p)
        {
            var parameterType = p.ParameterType;
            return injectable.Keys.Any(t => parameterType.IsAssignableFrom(t));
        }

        protected virtual void Invoke(MethodInfo methodInfo, object[] typedArgs, Dictionary<Type, object> injectable)
        {
            InjectArguments(methodInfo, injectable, ref typedArgs);
            var result = methodInfo.Invoke(Target, typedArgs);

            OnResult(result);
        }

        protected void InjectArguments(MethodInfo methodInfo, Dictionary<Type, object> injectable, ref object[] typedArgs)
        {
            var parameters = methodInfo.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramInfo = parameters[i];
                var parameterType = paramInfo.ParameterType;
                var typeKey = injectable.Keys.FirstOrDefault(t => parameterType.IsAssignableFrom(t));
                if (typeKey == null)
                    continue;

                typedArgs[i] = injectable[typeKey];
            }
        }
        #endregion

        #region Default Action

        protected virtual void OnCommandNotFound(string name, string[] args)
        {
            try
            {
                DefaultAction(name, args);
            }
            catch (Exception ex)
            {
                Error(ex.Message);
            }
        }

        protected virtual void DefaultActionHandler(string name, string[] args)
        {
            Error("Command " + name + " was not found");
        }

        #endregion

        #region Events

        protected virtual void OnResult(object result)
        {
            //no default action on result
        }

        protected virtual bool OnStackChanged()
        {
            StackChanged?.Invoke(Stack.Reverse());
            return false;
        }
        #endregion

        #region Argument Construction

        protected bool GetContext(IEnumerable<string> arguments, out string name, out string[] args)
        {
            var all = Stack.Reverse().Concat(arguments.Where(a => !string.IsNullOrWhiteSpace(a)));

            //leave last argument empty to indicate continuation, unless there is already one
            args = (arguments.LastOrDefault() == String.Empty ? all.Skip(1).Append(String.Empty) : all.Skip(1)).ToArray();
            name = all.FirstOrDefault();

            return !String.IsNullOrEmpty(name);
        }

        protected virtual object GetDefaultValue(ParameterInfo param)
        {
            var parameterType = param.ParameterType;
            if (typeof(RpcDispatcher).IsAssignableFrom(parameterType))
                return this;

            return param.HasDefaultValue ? param.RawDefaultValue : null;
        }

        protected virtual object ConvertArg(string args, ParameterInfo param)
        {
            var parameterType = param.ParameterType;
            var toConvert = args.Trim('"');

            //no conversion needed from string -> string
            if (parameterType == typeof(string))
                return toConvert;

            //allow y/n to be used with boolean
            if (parameterType == typeof(bool))
                if (toConvert.ToUpperInvariant().Equals("Y"))
                    return true;
                else if (toConvert.ToUpperInvariant().Equals("N"))
                    return false;

            var desc = TypeDescriptor.GetConverter(parameterType);

            if (desc != null && desc.CanConvertFrom(typeof(string)))
                return desc.ConvertFromString(toConvert);

            return Convert.ChangeType(toConvert, parameterType);
        }
        #endregion

        #region Help

        /// <summary>
        /// Gets the command attribute for the method.
        /// </summary>
        /// <param name="method">The method name.</param>
        /// <returns>null if not found</returns>
        public CommandAttribute GetCommandAttribute(string method)
        {
            if (!GetMethod(method, out var methodInfo))
                return null;

            return methodInfo.GetCustomAttributes<CommandAttribute>().FirstOrDefault();
        }

        /// <summary>
        /// Gets the command syntax for the specified method.
        /// </summary>
        public string GetSyntax(string name)
        {
            if (!GetMethod(name, out var methodInfo))
                return $"Command '{name}' not found";

            var paramText = string.Join(" ", methodInfo.GetParameters().Select(p => GetParamInfo(p)).DefaultIfEmpty("<no parameters>"));
            var attr = GetCommandAttribute(name);

            return $"{attr.CommandName} {paramText}";
        }

        /// <summary>
        /// Gets the help text for that parameter
        /// </summary>
        /// <param name="name">The name of the parameter.</param>
        public string GetParamHelp(string name)
        {
            if (!GetMethod(name, out var methodInfo))
                return String.Empty;

            var paramDescriptions =
            methodInfo
                .GetParameters()
                .SelectMany(parameter =>
                    parameter
                    .GetCustomAttributes<DescriptionAttribute>()
                    .Select(desc => $"-{parameter.Name}\t{desc.Description}")
                );

            return String.Join(Environment.NewLine, paramDescriptions);
        }

        private string GetParamInfo(ParameterInfo p)
        {
            if (CanInject(Inject, p))
                return $"";

            var underlying = Nullable.GetUnderlyingType(p.ParameterType);
            var paramTypeName = (underlying ?? p.ParameterType).Name + (underlying != null ? "?" : "");
            var paramSig = p.HasDefaultValue ? p.DefaultValue != null ? $"[{p.Name}] = {p.DefaultValue}" : $"[{p.Name}]" : p.Name;

            return $"({paramTypeName} {paramSig})";
        }

        /// <summary>
        /// Gets the full help text for that parameter.
        /// </summary>
        /// <param name="commandName">Name of the command.</param>
        /// <returns></returns>
        public string GetHelp(string commandName)
        {
            var attr = this.GetCommandAttribute(commandName);
            var paramHelp = this.GetParamHelp(commandName);
            return $"Command {attr?.CommandName} {attr?.HelpText} \nSyntax: {GetSyntax(commandName)}\n{paramHelp}";
        }

        /// <summary>
        /// List all commands and their descriptions
        /// </summary>
        /// <returns></returns>
        public string GetHelp()
        {
            var table = GetCommands().Select(command => $"{command,-20} {GetCommandAttribute(command)?.HelpText}");
            return String.Join("\n", table);
        }

        #endregion
    }
}
