using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace ImpossibLe
{
    public static class TailCall
    {
        /// <summary>
        /// Rewrite a tail recursive (but not using .NET tail recursion) 
        /// function of the form myFunc(argument, accumulator) into a 
        /// tail-recursive (with .NET tail recursion) form of same
        /// </summary>
        /// <typeparam name="A">The type of the argument to a simple recursive form of the function</typeparam>
        /// <typeparam name="R">The type of the result of the function</typeparam>
        /// <param name="nonTailRecursive">A function with a tail call, where
        /// the first argument is the argument to the non-tail-recursive
        /// form of the function, and the second is an accumulator for the 
        /// result</param>
        /// <returns></returns>
        public static Func<A, R, R> Rewrite<A, R>(object self, Func<A, R, R> nonTailRecursive)
        {
            MethodInfo         methodInfo         = nonTailRecursive.Method;
            string             assembly           = methodInfo.DeclaringType.Assembly.Location;
            string             assemblyExt        = System.IO.Path.GetExtension(assembly);
            string             tempFileName       = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), assemblyExt);
            AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(assembly);
            TypeDefinition     typeDefinition     = assemblyDefinition.MainModule.GetType(methodInfo.DeclaringType.FullName);
            assemblyDefinition.Name               = new AssemblyNameDefinition("Rewritten", new Version(1, 0));
            assemblyDefinition.MainModule.Name    = System.IO.Path.GetFileName(tempFileName);
            typeDefinition.Name = "RewrittenType";
            typeDefinition.Namespace = "RewrittenNamespace";
            //MethodDefinition recursiveCall = new MethodDefinition()
            MethodDefinition   definition         = typeDefinition.Methods.Single(m => m.Name == methodInfo.Name);
            MethodReference    methodReference    = assemblyDefinition.MainModule.Import(methodInfo);
            ILProcessor        ilProcessor        = definition.Body.GetILProcessor();
            definition.Body.SimplifyMacros();
            TailCallData tailCallData = TryFindTailCall(definition.Body.Instructions, definition);
            while (tailCallData != null)
            {
                // rewrite as tail call
                // Step 1: Remove stloc/ldloc/br.s instructions before ret instruction
                for (var index = tailCallData.RetIndex -1; index > tailCallData.CallIndex; index--)
                {
                    ilProcessor.Remove(definition.Body.Instructions[index]);
                }
                // Step 2: Rewrite any stloc/br "returns" as proper rets
                foreach (var otherExit in tailCallData.OtherExits)
                {
                    ilProcessor.Replace(otherExit.BrInstruction, Instruction.Create(OpCodes.Ret));
                    ilProcessor.Remove(otherExit.StlocInstruction);
                }

                // Step 3: Insert "tail" IL instruction before call
                ilProcessor.InsertBefore(tailCallData.CallInstruction, ilProcessor.Create(OpCodes.Tail));

                // any more?
                tailCallData = TryFindTailCall(definition.Body.Instructions, definition);
            }
            definition.Body.OptimizeMacros();
            assemblyDefinition.Write(tempFileName);
            Assembly rewrittenAssembly = Assembly.LoadFile(tempFileName);
            Type rewrittenType = rewrittenAssembly.GetType(definition.DeclaringType.FullName);
            MethodInfo rewrittenMethod = rewrittenType.GetMethod(
                definition.Name, 
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, 
                null,
                new[] { typeof(A), typeof(R) },
                null);
            Expression instance = self != null ? Expression.Constant(self) : null;
            Func<A, R, R> result = (Func<A, R, R>)Delegate.CreateDelegate(typeof(Func<A, R, R>), rewrittenMethod);
            return result;
        }

        private class StlocBrReturnData
        {
            /// <summary>
            /// The stloc instruction which stores the value to be returned prior to the br jump to the ldloc before the ret at the end of the method
            /// </summary>
            public readonly Instruction StlocInstruction;

            /// <summary>
            /// Index of the br instruction which jumps to the "real" ldloc/ret at the end of the method within method.Body.Instructions collection
            /// </summary>
            public readonly Instruction BrInstruction; 

            public StlocBrReturnData(Instruction stlocInstruction, Instruction brInstruction)
            {
                this.StlocInstruction = stlocInstruction;
                this.BrInstruction = brInstruction;
            }
        }

        private class TailCallData
        {
            /// <summary>
            /// The index of CallInstruction within method.Body.Instructions collection
            /// </summary>
            public readonly int CallIndex;

            /// <summary>
            /// The call instruction we want to convert to a tail call
            /// </summary>
            public readonly Instruction CallInstruction;

            /// <summary>
            /// The ld instruction used to put the result value on the stack
            /// </summary>
            public readonly Instruction LdInstruction;

            /// <summary>
            /// Other places in the IL that correspond to a C# "return" line, comprised of a stloc 
            /// and then a br to the ldloc/ret instructions at the end of the method
            /// </summary>
            public readonly IEnumerable<StlocBrReturnData> OtherExits;

            /// <summary>
            /// The index of the ret following the tail call within method.Body.Instructions collection
            /// </summary>
            public readonly int RetIndex;

            public TailCallData(
                int callIndex, 
                Instruction callInstruction, 
                Instruction ldInstruction, 
                IEnumerable<StlocBrReturnData> otherExits, 
                int retIndex)
            {
                this.CallIndex = callIndex;
                this.CallInstruction = callInstruction;
                this.LdInstruction = ldInstruction;
                this.OtherExits = otherExits;
                this.RetIndex = retIndex;
            }
        }

        private static TailCallData TryFindTailCall(Mono.Collections.Generic.Collection<Instruction> instructions, MethodDefinition method)
        {
            foreach (var item in instructions.Select((instruction, index) => new { instruction, index }))
            {
                TailCallData tailCallData = TryGetTailCallData(item.index, method);
                if (tailCallData != null)
                {
                    return tailCallData;
                }
            }
            return null;
        }

        private static OpCode[] callInstructions = new[] {
            OpCodes.Call,
            OpCodes.Calli,
            OpCodes.Callvirt
        };

        // Tries to find a call which is a tail call in form but not already an IL tail call
        private static TailCallData TryGetTailCallData(int index, MethodDefinition method)
        {
            Instruction instruction = method.Body.Instructions[index];
            if (index == 0 && instruction.OpCode != OpCodes.Tail)
            {
                // if we're at the first instruction and it's not tail, then we're not on a tail call
                return null;
            }
            if (!callInstructions.Contains(instruction.OpCode)) 
            {
                // If it's not a call, then it's not a tail call
                return null;
            }
            Instruction previousInstruction = method.Body.Instructions[index - 1];
            if (previousInstruction.OpCode == OpCodes.Tail)
            {
                // If it's already a tail call then we have nothing to do.
                return null;
            }
            if (instruction.Operand != method)
            {
                // calls to different methods are not tail calls
                return null;
            }
            int retIndex = TryFindTailRetIndex(index, method);
            if (retIndex <= 0)
            {
                // If there's no ret, it's not a tail call
                return null;
            }
            Instruction ldInstruction = TryFindLdInstruction(index, retIndex, method);
            if (ldInstruction == null)
            {
                // If we can't find the local used to store the returned value, we can't tail call optimize it
                return null;
            }
            var otherExits = FindOtherExits(index, ldInstruction, method);
            return new TailCallData(index, instruction, ldInstruction, otherExits, retIndex);
        }

        private static Instruction TryFindLdInstruction(int callIndex, int retIndex, MethodDefinition method)
        {
            for (int i = retIndex - 1; i > callIndex; i--)
            {
                if (method.Body.Instructions[i].OpCode == OpCodes.Ldloc)
                {
                    return method.Body.Instructions[i];
                }
            }
            return null;
        }

        private static IEnumerable<StlocBrReturnData> FindOtherExits(int searchBackwardsFrom, Instruction ldInstruction, MethodDefinition method)
        {
            var otherExits = new List<StlocBrReturnData>();
            for (int index = searchBackwardsFrom; index > 1; index--)
            {
                Instruction instruction = method.Body.Instructions[index];
                if ((instruction.OpCode == OpCodes.Br)
                    && ((instruction.Operand as Instruction) == ldInstruction))
                {
                    if (method.Body.Instructions[index - 1].OpCode == OpCodes.Stloc)
                    {
                        otherExits.Add(new StlocBrReturnData(method.Body.Instructions[index - 1], instruction));
                    }
                }
            }
            return otherExits;
        }

        private static OpCode[] instructionsAllowedInBetweenCallAndRet = new[] {
            OpCodes.Ldloc,
            OpCodes.Stloc,
            OpCodes.Nop
        };


        private static int TryFindTailRetIndex(int callIndex, MethodDefinition method)
        {
            for (var index = callIndex + 1; index < method.Body.Instructions.Count; index++)
            {
                Instruction currentInstruction = method.Body.Instructions[index];
                if (currentInstruction.OpCode == OpCodes.Ret)
                {
                    return index;
                }
                if (IsDoNothingBreak(currentInstruction))
                {
                    // Sometimes C# compiler emits a br.s to following line. Whatcha gonna do?
                    // Ignore it, I guess?
                    continue;
                }
                if (!(instructionsAllowedInBetweenCallAndRet.Contains(currentInstruction.OpCode)))
                {
                    // If we're doing non-allowed stuff before ret then it's not a tail call
                    return -1;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns true when instruction is a br or br.s which jumps to the instruction immediately following it.
        /// </summary>
        private static bool IsDoNothingBreak(Instruction instruction)
        {
            Instruction operand = instruction.Operand as Instruction;
            if (operand == null)
            {
                return false;
            }
            return (instruction.OpCode == OpCodes.Br || instruction.OpCode == OpCodes.Br_S)
                && operand.Previous == instruction;
        }
    }
}
