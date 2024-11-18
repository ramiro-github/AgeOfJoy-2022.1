using System;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;

class CommandIFTHEN : ICommandBase
{
    public string CmdToken { get; } = "IFTHEN";
    public CommandType.Type Type { get; } = CommandType.Type.Command;

    CommandExpression expr;

    ICommandBase cmd; //if it is
    ICommandBase cmdElse;
    ConfigurationCommands config;
    public CommandIFTHEN(ConfigurationCommands config)
    {
        this.config = config;
        expr = new(config);

    }

    public bool Parse(TokenConsumer tokens)
    {
        // if (tokens.Token != "(")
        //     throw new Exception($"malformed IF/THEN, expression should be enclosed by ()");

        expr.Parse(tokens);
        
        if (tokens.Token.ToUpper() != "THEN")
            throw new Exception($"malformed IF/THEN, THEN is missing");

        cmd = Commands.GetNew(tokens.Next(), config);
        if (cmd == null)
            throw new Exception($"Syntax error command not found in THEN clause: {tokens.ToString()}");
        
        tokens++;
        cmd.Parse(tokens);
        if (tokens.Token == ":")
        {
            //is not the only sentence in THEN, add all of them to the program.
            config.LineNumber += AGEProgram.MinJump; //actual number increment.
            CommandGOTO goTo = new CommandGOTO(this.config);
            goTo.SetJumpLineNumber(new BasicValue(config.LineNumber));
            
            config.ageProgram.AddCommand(config.LineNumber, cmd); //add the first command of the sentence.

            cmd = goTo;// replace the command to a goto for THEN.

            while (tokens.Token == ":")
            {
                tokens++;
                ICommandBase cmdThen = Commands.GetNew(tokens.Token, config);
                if (cmdThen == null || cmdThen.Type != CommandType.Type.Command)
                    throw new Exception($"Syntax error command not found in THEN sentence: {tokens.Token}  line: {(int)config.LineNumber}");

                config.LineNumber += AGEProgram.MinJump; //actual number increment.
                config.ageProgram.AddCommand(config.LineNumber, cmdThen);

                cmdThen.Parse(++tokens);
            }
            AddGoToNextUserLine();
        }

        if (tokens.Token.ToUpper() == "ELSE")
        {

            cmdElse = Commands.GetNew(tokens.Next(), config);
            if (cmd == null)
                throw new Exception($"Syntax error command not found in ELSE clause: {tokens.ToString()}");
            
            tokens++;
            cmdElse.Parse(tokens);
            if (tokens.Token == ":")
            {
                //is not the only sentence in THEN, add all of them to the program.
                config.LineNumber += AGEProgram.MinJump; //actual number increment.
                CommandGOTO goTo = new CommandGOTO(this.config);
                goTo.SetJumpLineNumber(new BasicValue(config.LineNumber));

                config.ageProgram.AddCommand(config.LineNumber, cmdElse); //add the first command of the sentence.

                cmdElse = goTo;// replace the command to a goto for ELSE.

                while (tokens.Token == ":")
                {
                    tokens++;
                    ICommandBase cmdElse = Commands.GetNew(tokens.Token, config);
                    if (cmdElse == null || cmdElse.Type != CommandType.Type.Command)
                        throw new Exception($"Syntax error command not found in ELSE sentence: {tokens.Token}  line: {(int)config.LineNumber}");

                    config.LineNumber += AGEProgram.MinJump; //actual number increment.
                    config.ageProgram.AddCommand(config.LineNumber, cmdElse);

                    cmdElse.Parse(++tokens);
                }
                AddGoToNextUserLine();

            }
        }

        return true;
    }
    private void AddGoToNextUserLine()
    {
        CommandGOTO goTo = new CommandGOTO(this.config);
        goTo.SetJumpLineNumber(new BasicValue((int)config.LineNumber + 1));
        config.LineNumber += AGEProgram.MinJump; //actual number increment.
        config.ageProgram.AddCommand(config.LineNumber, goTo);
    }

    public BasicValue Execute(BasicVars vars)
    {
        AGEBasicDebug.WriteConsole($"[AGE BASIC RUN {CmdToken}] [{expr}] ");

        BasicValue condition = expr.Execute(vars);
        if (condition.IsTrue())
            return cmd.Execute(vars);

        if (cmdElse != null)
            return cmdElse.Execute(vars);

        return null;
    }

}