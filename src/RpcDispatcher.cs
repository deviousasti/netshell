using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NetShell
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class CommandAttribute : Attribute
    {
        public string CommandName { get; }

        public string HelpText { get; set; }

        public CommandAttribute()
        {
        }

        public CommandAttribute(string methodName)
        {
            CommandName = methodName;
        }

        public CommandAttribute(string commandName, string helpText)
        {
            CommandName = commandName;
            HelpText = helpText;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class SuggestAttribute : Attribute
    {
        public string MethodName { get; }

        public SuggestAttribute(string methodName)
        {
            this.MethodName = methodName;
        }
    }

    public class RpcDispatcher
    {
        public string StackPushCommand { get; set; } = "\\";

        public string StackPopCommand { get; set; } = "..";

        public string StackClearCommand { get; set; } = "...";

        public object Target { get; }

        protected Dictionary<Type, Object> Inject { get; }

        private Dictionary<string, MethodInfo> LookupTable { get; }

        public Action<string> Error { get; set; } = text => ConsoleUtil.Error(text);

        public Stack<string> Stack { get; set; }

        public event Action<IEnumerable<string>> StackChanged;

        public IEnumerable<string> GetCommands()
        {
            return this.LookupTable.Keys.Select(k => k.ToLowerInvariant());
        }

        public RpcDispatcher(object target)
        {
            Target = target;
            Stack = new Stack<string>();
            Inject = new Dictionary<Type, object>() {
                { typeof(RpcDispatcher), this },
                { typeof(CancellationToken), default }
            };

            var attributes =
                target.GetType()
                .GetMethods()
                .SelectMany(method => method.GetCustomAttributes<CommandAttribute>().Select(attr => (method, attr)));

            try
            {
                LookupTable = attributes.ToDictionary(pair => (pair.attr.CommandName ?? pair.method.Name).ToUpperInvariant(), pair => pair.method);
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

        public void Register(Type type, object obj) => Inject[type] = obj;

        public void Register<T>(T obj) => Register(typeof(T), obj);

        public T GetInstance<T>() => (T)Inject[typeof(T)];

        public bool TryParse(string text, out string[] args) => TryParse(new StringReader(text), out args);

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
                    args = parser.ReadFields();
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

        protected bool IsFlag(string arg) => arg != null && arg.StartsWith("-");

        protected ParameterInfo FindParameterFor(string flagName, ParameterInfo[] parameters)
        {
            var name = flagName.Replace('-', '\0');
            return Array.Find(parameters, p => p.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
        }

        public bool Dispatch(string input) => Dispatch(new StringReader(input));

        public bool Dispatch(TextReader reader)
        {
            if (!TryParse(reader, out var args))
                return false;

            return Dispatch(args);
        }

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
                OnCommandNotFound(name);
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

                    if (IsFlag(arg))
                    {
                        var position = Array.IndexOf(parameters, FindParameterFor(arg, parameters));
                        if (position == -1)
                        {
                            Error($"No parameter named {arg} was found");
                            return false;
                        }

                        var param = parameters[position];

                        if (!usedParams.Add(param))
                        {
                            Error($"Parameter {arg} was specified more than once");
                            return false;
                        }

                        if (IsFlag(next) || next == null)
                        {
                            if(param.ParameterType != typeof(bool))
                            {
                                Error($"A value was not specified for -{param.Name}");
                                return false;
                            }

                            //attempted to use as a switch, rather than 
                            //a parameter with a value
                            typedArgs[position] = ConvertArg("true", param);                            
                            continue;
                        }

                        //consider next argument taken
                        typedArgs[position] = ConvertArg(next, param);
                        i += 1; //skip ahead
                        continue;
                    }

                    if (!usedParams.Add(parameters[param_order]))
                    {
                        Error($"The parameter for {arg} was already specified at position ({param_order})");
                        return false;
                    }


                    //regular parameter order
                    typedArgs[param_order] = ConvertArg(arg, parameters[param_order]);
                    param_order += 1;
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

                foreach (var parameter in parameters)
                {
                    var parameterType = parameter.ParameterType;

                    if (parameterType.IsEnum)
                    {
                        Error($"Valid values for {parameterType.Name}: {String.Join(", ", Enum.GetNames(parameterType))}");
                    }
                }

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

        protected virtual void OnCommandNotFound(string name)
        {
            Error("Command " + name + " was not found");
        }

        protected virtual void OnResult(object result)
        {

        }

        public bool GetMethod(string name, out MethodInfo methodInfo)
        {
            return LookupTable.TryGetValue((name ?? string.Empty).ToUpperInvariant(), out methodInfo);
        }

        public bool GetContext(IEnumerable<string> arguments, out string name, out string[] args)
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

            if (parameterType == typeof(string))
                return toConvert;

            var desc = TypeDescriptor.GetConverter(parameterType);

            if (desc != null && desc.CanConvertFrom(typeof(string)))
                return desc.ConvertFromString(toConvert);

            return Convert.ChangeType(toConvert, parameterType);
        }

        public CommandAttribute GetAttribute(string method)
        {
            if (!GetMethod(method, out var methodInfo))
                return null;

            return methodInfo.GetCustomAttributes<CommandAttribute>().FirstOrDefault();
        }

        public string GetSyntax(string name)
        {
            if (!GetMethod(name, out var methodInfo))
                return $"Command '{name}' not found";

            var paramText = string.Join(" ", methodInfo.GetParameters().Select(p => GetParamInfo(p)).DefaultIfEmpty("<no parameters>"));
            var attr = GetAttribute(name);

            return $"{attr.CommandName} {paramText}";
        }

        private string GetParamInfo(ParameterInfo p)
        {
            if (CanInject(Inject, p))
                return $"";

            return $"({p.ParameterType.Name} {(p.HasDefaultValue ? $"[{p.Name}] = {p.DefaultValue}" : p.Name)})";
        }

        protected bool OnStackChanged()
        {
            StackChanged?.Invoke(Stack.Reverse());
            return false;
        }

        public string GetHelp(string commandName)
        {
            var attr = this.GetAttribute(commandName);
            return $"Command {attr?.CommandName} {attr?.HelpText} \nSyntax: {GetSyntax(commandName)}";
        }

        public string GetHelp()
        {
            var table = GetCommands().Select(command => $"{command.PadRight(20)} {GetAttribute(command)?.HelpText}");
            return String.Join("\n", table);
        }
    }
}
