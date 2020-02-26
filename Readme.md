# netshell

Create a unix-like shell for your APIs

## 10-second intro

netshell maps methods into shell commands.

```csharp
[Command("echo")]
public void Echo(string text, ConsoleColor color = ConsoleColor.White)
{
	Console.ForegroundColor = color;
	Console.WriteLine(text);
}
```

Gets you this:

![netshell](https://user-images.githubusercontent.com/2375486/75217204-57feb580-57bc-11ea-8211-91842a631c3b.gif)

## Features 

* Auto-completion
* Suggestions
* Fuzzy matching
* Named parameters
* Object printer
* Async support
* Dependency injection
* Help generation
* Stacked commands
* and more

## Usage

### Basics
Add the `Command([name], [description])` attribute to a method:

```csharp
class Commands 
{
    [Command("echo", "Print out the text to standard out")]
    void Echo(string text, ConsoleColor color = ConsoleColor.White)
    { 
    }
}
```
In your `Main` method:

```csharp
static int Main(string[] args)
 {
   var shell = new NetShell.RpcShell(new Commands()) { Prompt = "sh" };
   return shell.Run();
 }
```
or, if you have nothing to customize:

```csharp
static int Main(string[] args) => NetShell.RpcShell.Run<Commands>();
```

### Suggestions

To create auto-complete suggestions for parameters, add a `Suggest` attribute with the name of the method that must be called to get suggestions. The method is expected to return an enumerable of strings.

```csharp
[Command("connect")]
public void Connect(
	[Suggest(nameof(ListPorts))] string port = default,
	[Suggest(nameof(BaudRates))] int baud = 9600,            
)
{
}

public string[] ListPorts() => SerialPort.GetPortNames();
public string[] BaudRates() => "9600, 14400, 19200, 38400, 57600, 115200".Split(',');

```

### Results

You can either choose to have a `void` method and use `Console.WriteLine`, or you can return a value from the method. Return values will be displayed with the best-matching object printer.

```csharp
[Command]
public Person[] GetPeople()
{
	return new Person[] { 
		new Person { Age = 10, Name = "Asti" }, 
		new Person { Age = 11, Name = "Johny" } 
	};
}
```

and when you run this:

```
>getpeople
 ---------------
 | Age | Name  |
 ---------------
 | 10  | Asti  |
 ---------------
 | 11  | Johny |
 ---------------
```

### Dependency injection

You can do use `RpcShell.Register` to register a new dependency to be injected.
Whenever a method is being called, it will inject all registered types as parameters, and these injected parameters will not show up as part of your command syntax.

For example, if you do
```csharp
shell.Register(new HttpClient());
```
in your method you can use `HttpClient` as a parameter, and it will be injected in.
```csharp
[Command("validate-phone")]
public Task<string> Validate(string number, HttpClient client) => 	
	client.GetStringAsync($"https://numvalidate.com/api/validate?number={number}");            
```
The shell itself can be injected. For example, to change the current prompt from within a command:

```
[Command("cd")]
public void ChangeDir(string path, Shell shell)
{
	shell.Prompt = Environment.CurrentDirectory = path;
}
```

### Async

You can write `async` methods as you would normally. 

```csharp
[Command("validate-phone")]
public async Task<string> Validate(string number)
{
    var response = await new HttpClient().GetAsync($"https://numvalidate.com/api/validate?number={number}");
    var json = await response.Content.ReadAsStringAsync();
    return json;
}
```

You can also have cancellation. 

```csharp
[Command("validate-phone")]
public async Task<string> Validate(string number, CancellationToken cancellation)
{
    var response = await new System.Net.Http.HttpClient().GetAsync($"https://numvalidate.com/api/validate?number={number}", cancellation);
    var json = await response.Content.ReadAsStringAsync();
    return json;
}
```

The `CancellationToken` is injected and has no impact on your commands' syntax. See [this](https://user-images.githubusercontent.com/2375486/75220698-0824ec00-57c6-11ea-9df9-bdc11e705f85.gif).

Pressing any key cancels the executing command.

```
> help validate-phone
Command validate-phone
Syntax: validate-phone (String number)
```



## Why use netshell?

netshell was developed as a way for ops to have a simplified command-line interface into the application internals without having to know implementation details. Most of the methods you want to export are likely already written as part of tests. 

A great alternative for .net applications is to use PowerShell, it allows for easy scripting of common tasks. Unfortunately, PowerShell there are some significant hurdles:

- Mix of scripts and assemblies, type loading issues
- Async is difficult
- Suggestions are non-trivial to implement
- Porting existing C# code is tricky, especially when it uses [closures](https://docs.microsoft.com/en-us/dotnet/api/system.management.automation.scriptblock.getnewclosure?redirectedfrom=MSDN&view=pscore-6.2.0#System_Management_Automation_ScriptBlock_GetNewClosure)