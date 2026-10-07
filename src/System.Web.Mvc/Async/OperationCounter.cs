// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;

namespace System.Web.Mvc.Async
{
    public sealed class OperationCounter
    {
        private int _count;
#if NET10_0_OR_GREATER
        internal AspNetWebStack.Native.NativeLegacyAsyncOperation NativeOperation { get; set; }
#endif

        public event EventHandler Completed;

        public int Count
        {
            get { return Thread.VolatileRead(ref _count); }
        }

        private int AddAndExecuteCallbackIfCompleted(int value)
        {
            int newCount = Interlocked.Add(ref _count, value);
            if (newCount == 0)
            {
                OnCompleted();
            }

            return newCount;
        }

        public int Decrement()
        {
            return AddAndExecuteCallbackIfCompleted(-1);
        }

        public int Decrement(int value)
        {
            return AddAndExecuteCallbackIfCompleted(-value);
        }

        public int Increment()
        {
            return AddAndExecuteCallbackIfCompleted(1);
        }

        public int Increment(int value)
        {
            return AddAndExecuteCallbackIfCompleted(value);
        }

        private void OnCompleted()
        {
#if NET10_0_OR_GREATER
            if (NativeOperation != null)
            {
                NativeOperation.Notify(() => { var nativeHandler = Completed; if (nativeHandler != null) nativeHandler(this, EventArgs.Empty); }, false);
                return;
            }
#endif
            EventHandler handler = Completed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
