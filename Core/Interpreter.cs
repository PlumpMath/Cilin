﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AshMind.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Cilin.Core.Internal;
using System.Reflection;

namespace Cilin.Core {
    public class Interpreter {
        private readonly IReadOnlyDictionary<OpCode, ICilHandler> _handlers;
        private readonly MethodInvoker _invoker;
        private readonly Resolver _resolver;
        
        public Interpreter(IDictionary<AssemblyDefinition, string> assemblyPathMap = null) : this(
            assemblyPathMap ?? new Dictionary<AssemblyDefinition, string>(),
            typeof(Interpreter).Assembly
                .GetTypes()
                .Where(t => t.HasInterface<ICilHandler>() && !t.IsInterface)
                .Select(t => (ICilHandler)Activator.CreateInstance(t))
        ) {}

        private Interpreter(IDictionary<AssemblyDefinition, string> assemblyPathMap, IEnumerable<ICilHandler> handlers) {
            var handlersByCode = new Dictionary<OpCode, ICilHandler>();
            foreach (var handler in handlers) {
                foreach (var opCode in handler.GetOpCodes()) {
                    handlersByCode.Add(opCode, handler);
                }
            }
            _handlers = handlersByCode;
            _invoker = new MethodInvoker(this);
            _resolver = new Resolver(_invoker, assemblyPathMap);
        }

        public object InterpretCall(MethodDefinition method, IReadOnlyList<object> arguments)
            => InterpretCall(Empty<TypeReference>.Array, method, Empty<TypeReference>.Array, null, arguments);

        public object InterpretCall(MethodDefinition method, object target, IReadOnlyList<object> arguments)
            => InterpretCall(Empty<TypeReference>.Array, method, Empty<TypeReference>.Array, target, arguments);

        public object InterpretCall(IReadOnlyList<Type> declaringTypeArguments, MethodBase method, MethodDefinition definition, IReadOnlyList<Type> typeArguments, object target, IReadOnlyList<object> arguments) {
            ValidateCall(definition, declaringTypeArguments, typeArguments);
            var genericScope = GenericScope.None
                .With(definition.DeclaringType.GenericParameters, declaringTypeArguments)
                .With(definition.GenericParameters, typeArguments);

            return InterpretCall(genericScope, method, definition, target, arguments);
        }

        public object InterpretCall(IReadOnlyList<TypeReference> declaringTypeArguments, MethodDefinition definition, IReadOnlyList<TypeReference> typeArguments, object target, IReadOnlyList<object> arguments) {
            ValidateCall(definition, declaringTypeArguments, typeArguments);
            var genericTypeScope = GenericScope.None
                .With(definition.DeclaringType.GenericParameters, declaringTypeArguments.Select(a => _resolver.Type(a, GenericScope.None)));
            var genericScope = genericTypeScope.With(definition.GenericParameters, typeArguments.Select(a => _resolver.Type(a, genericTypeScope)));

            var method = _resolver.Method(definition, genericScope);
            return InterpretCall(genericScope, method, definition, target, arguments);
        }

        private object InterpretCall(GenericScope genericScope, MethodBase method, MethodDefinition definition, object target, IReadOnlyList<object> arguments) {
            var context = new CilHandlerContext(genericScope, method, definition, target, arguments ?? Empty<object>.Array, _resolver, _invoker);
            var instruction = definition.Body.Instructions[0];

            var returnType = (method as MethodInfo)?.ReturnType;

            while (instruction != null) {
                if (instruction.OpCode == OpCodes.Ret) {
                    var result = (object)null;
                    if (returnType != null && returnType != typeof(void))
                        result = TypeSupport.Convert(context.Stack.Pop(), returnType);

                    if (context.Stack.Count > 0)
                        throw new Exception($"Unbalanced stack on return: {context.Stack.Count} extra items.");

                    return result;
                }

                context.NextInstruction = instruction.Next;
                InterpretInstruction(instruction, context);
                instruction = context.NextInstruction;
            }

            throw new Exception($"Failed to reach a 'ret' instruction.");
        }

        private void ValidateCall(MethodDefinition method, IReadOnlyList<object> declaringTypeArguments, IReadOnlyList<object> typeArguments) {
            if (method.IsInternalCall)
                throw new ArgumentException($"Cannot interpret InternalCall method {method}.", nameof(method));
            if (!method.HasBody)
                throw new ArgumentException($"Cannot interpret method {method} that has no Body.", nameof(method));
            if (declaringTypeArguments.Count != method.DeclaringType.GenericParameters.Count)
                throw new ArgumentException($"Type {method.DeclaringType} requires {method.DeclaringType.GenericParameters.Count} type arguments, but got {declaringTypeArguments.Count}.");
            if (typeArguments.Count != method.GenericParameters.Count)
                throw new ArgumentException($"Method {method} requires {method.GenericParameters.Count} type arguments, but got {typeArguments.Count}.");
        }

        private void InterpretInstruction(Instruction instruction, CilHandlerContext context) {
            var handler = _handlers.GetValueOrDefault(instruction.OpCode);
            if (handler == null)
                throw new NotImplementedException($"Instruction {instruction.OpCode} is not implemented.");

            handler.Handle(instruction, context);
        }
    }
}
