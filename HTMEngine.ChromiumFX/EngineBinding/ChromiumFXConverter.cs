﻿using System;
using Chromium.Remote;
using HTMEngine.ChromiumFX.Convertion;
using MVVM.HTML.Core.Infra;
using MVVM.HTML.Core.JavascriptEngine.JavascriptObject;

namespace HTMEngine.ChromiumFX.EngineBinding 
{
    internal class ChromiumFXConverter : IJavascriptObjectConverter
    {
        private readonly CfrV8Context _CfrV8Context;
        internal ChromiumFXConverter(CfrV8Context context) 
        {
            _CfrV8Context = context;
        }

        public bool GetSimpleValue(IJavascriptObject decoratedValue, out object res, Type iTargetType = null) 
        {
            res = null;
            var value = decoratedValue.Convert();

            if ((value.IsUndefined) || (value.IsNull)) 
            {
                return true;
            }

            if (value.IsString) 
            {
                res = value.StringValue;
                return true;
            }

            if (value.IsBool) 
            {
                res = value.BoolValue;
                return true;
            }

            if (iTargetType.IsUnsigned()) 
            {
                if (value.IsUint)
                    res = value.UintValue;
            }
            else 
            {
                if (value.IsInt)
                    res = value.IntValue;
            }

            if ((res == null) && (value.IsDouble)) 
            {
                res = value.DoubleValue;
            }

            if (res != null) 
            {
                if (iTargetType != null)
                    res = Convert.ChangeType(res, iTargetType);

                return true;
            }

            if (value.IsDate) 
            {
                res = value.DateValue;
                return true;
            }

            return false;
        }
    }
}