using System;
using System.Collections.Generic;
using System.IO;

class CommandGOTO : ICommandBase
{
    public string CmdToken { get; } = "GOTO";
    public CommandType.Type Type { get; } = CommandType.Type.Command;

    CommandExpression expr;
    ConfigurationCommands config;
    BasicValue lineNumber = null;
    public CommandGOTO(ConfigurationCommands config)
    {
        this.config = config;
        expr = new(config);
    }
    public bool Parse(TokenConsumer tokens)
    {
        expr.Parse(tokens);
        return true;
    }

    public BasicValue Execute(BasicVars vars)
    {
        AGEBasicDebug.WriteConsole($"[AGE BASIC RUN {CmdToken}] [{expr}] ");

        if (lineNumber == null)
            lineNumber = expr.Execute(vars);

        config.JumpTo = (int) lineNumber.GetValueAsNumber(); 

        // AGEBasicDebug.WriteConsole($"[AGE BASIC RUN {CmdToken}] Jump to {config.JumpTo} ");

        return null;
    }

    public void SetJumpLineNumber(BasicValue lineNo)
    {
        lineNumber = lineNo;
    }

}