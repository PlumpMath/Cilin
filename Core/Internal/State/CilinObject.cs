﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Cilin.Core.Internal.Reflection;

namespace Cilin.Core.Internal.State {
    public class CilinObject : BaseData, INonRuntimeObject {
        public CilinObject(InterpretedType objectType) {
            if (objectType.IsAbstract || objectType.IsInterface)
                throw new ArgumentException($"{nameof(CilinObject)} must have a concrete type (provided {objectType})", nameof(objectType));

            ObjectType = objectType;
        }

        public InterpretedType ObjectType { get; }

        Type INonRuntimeObject.Type => ObjectType;
    }
}
