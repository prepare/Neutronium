﻿using System.Threading.Tasks;
using Neutronium.Core;
using Neutronium.Core.Navigation;
using Neutronium.WPF.Internal;
using System;

namespace Neutronium.WPF
{
    public class HTMLWindow : HTMLControlBase, INavigationSolver
    {
        public HTMLWindow() : this(new NavigationBuilder())
        {
        }

        public HTMLWindow(IUrlSolver urlSolver) : base(urlSolver)
        {
            NavigationBuilder = urlSolver as INavigationBuilder;
        }
      
        public INavigationBuilder NavigationBuilder { get; }

        public Task<IHTMLBinding> NavigateAsync(object viewModel, string id = null, JavascriptBindingMode mode = JavascriptBindingMode.TwoWay)
        {
            if (IsLoaded)
            {
                return NavigateAsyncBase(viewModel, id, mode);
            }

            var taskCompletion = new TaskCompletionSource<IHTMLBinding>();
            this.Loaded += (o,e) => HTMLWindow_Loaded(taskCompletion, viewModel, id, mode);
            return taskCompletion.Task;
        }

        private async void HTMLWindow_Loaded(TaskCompletionSource<IHTMLBinding> taskCompletion, object viewModel, string id, JavascriptBindingMode mode)
        {
            try
            {
                var res = await NavigateAsyncBase(viewModel, id, mode);
                taskCompletion.TrySetResult(res);
            }
            catch(Exception ex)
            {
                taskCompletion.SetException(ex);
            }       
        }
    }
}
