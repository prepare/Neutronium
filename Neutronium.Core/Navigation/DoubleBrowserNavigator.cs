﻿using System;
using System.Threading.Tasks;
using Neutronium.Core.Binding;
using Neutronium.Core.Exceptions;
using Neutronium.Core.Infra;
using Neutronium.Core.JavascriptFramework;
using Neutronium.Core.Navigation.Window;
using Neutronium.Core.WebBrowserEngine.Control;
using Neutronium.Core.WebBrowserEngine.Window;
using Neutronium.Core.Log;

namespace Neutronium.Core.Navigation
{
    public class DoubleBrowserNavigator : INavigationSolver 
    {
        private readonly IWebViewLifeCycleManager _WebViewLifeCycleManager;
        private readonly IJavascriptFrameworkManager _javascriptFrameworkManager;
        private readonly IUrlSolver _UrlSolver;        
        private IWebBrowserWindowProvider _CurrentWebControl;
        private IWebBrowserWindowProvider _NextWebControl;
        private IHTMLBinding _HTMLBinding;
        private IWebSessionLogger _webSessionLogger = new BasicLogger();
        private bool _Disposed = false;
        private bool _Navigating = false;
        private bool _UseINavigable = false;
        private HTMLLogicWindow _Window;

        public Uri Url { get; private set; }
        public IWebBrowserWindowProvider WebControl => _CurrentWebControl;
        public IWebBrowserWindow HTMLWindow => _CurrentWebControl?.HTMLWindow;
        public bool UseINavigable 
        {
            get { return _UseINavigable; }
            set { _UseINavigable = value; }
        }

        public IWebSessionLogger WebSessionLogger
        {
            set { _webSessionLogger = value; }
        }

        public DoubleBrowserNavigator(IWebViewLifeCycleManager lifecycler, IUrlSolver urlSolver, 
                                        IJavascriptFrameworkManager javascriptFrameworkManager)
        {
            _webSessionLogger = new BasicLogger();
            _javascriptFrameworkManager = javascriptFrameworkManager;
            _WebViewLifeCycleManager = lifecycler;
            _UrlSolver = urlSolver;
        }

        private void ConsoleMessage(object sender, ConsoleMessageArgs e)
        { 
            try
            {
                _webSessionLogger.LogBrowser(e, Url);
            }
            catch { }
        }

        private IHTMLBinding Binding
        {
            get { return _HTMLBinding; }
            set
            {
                _HTMLBinding?.Dispose();

                _HTMLBinding = value;

                if (_Disposed && (_HTMLBinding!=null))
                   Binding = null;
            }
        }

        private void FireNavigate(object inewvm, object ioldvm=null) 
        {
            OnNavigate?.Invoke(this, new NavigationEvent(inewvm, ioldvm));
        }

        private void FireLoaded(object iloadedvm)
        {
            OnDisplay?.Invoke(this, new DisplayEvent(iloadedvm));
        }

        private void Switch(Task<IHTMLBinding> iBinding, HTMLLogicWindow iwindow, TaskCompletionSource<IHTMLBinding> tcs)
        {
            var oldvm = Binding?.Root;
            var fireFirstLoad = false;
            Binding = iBinding.Result;
          
            if (_CurrentWebControl!=null)
            {
                _CurrentWebControl.HTMLWindow.ConsoleMessage -= ConsoleMessage;
                _CurrentWebControl.Dispose();
            }
            else 
            {
                fireFirstLoad = true;
            }

            _CurrentWebControl = _NextWebControl;     
            _NextWebControl = null;
            _CurrentWebControl.HTMLWindow.Crashed += Crashed;

            _CurrentWebControl.Show();
    
            _Window = iwindow; 

            var inav = _UseINavigable ? Binding.Root as INavigable : null;
            if (inav != null)
                inav.Navigation = this;
            _Window.State = WindowLogicalState.Opened;

            _Window.OpenAsync().ContinueWith(t => EndAnimation(Binding.Root));

            _Navigating = false;

            if (fireFirstLoad)
                OnFirstLoad?.Invoke(this, EventArgs.Empty);

            FireNavigate(Binding.Root, oldvm);

            tcs?.SetResult(Binding);
        }    

        private void EndAnimation(object inavgable)
        {
            _WebViewLifeCycleManager.GetDisplayDispatcher()
                .RunAsync( () => FireLoaded(inavgable) );
        }

        private void Crashed(object sender, BrowserCrashedArgs e)
        {
            var dest = _CurrentWebControl.HTMLWindow.Url;
            var vm = Binding.Root;
            var mode = Binding.Mode;

            _webSessionLogger.Error("WebView crashed trying recover");

            CleanWebControl(ref _CurrentWebControl);
            Binding = null;

            Navigate(dest, vm, mode);
        }

        private Task<IHTMLBinding> Navigate(Uri uri, object iViewModel, JavascriptBindingMode iMode = JavascriptBindingMode.TwoWay)
        {
            if (uri == null)
                throw ExceptionHelper.GetArgument($"ViewModel not registered: {iViewModel.GetType()}");

            _Navigating = true;

            var oldvm = Binding?.Root as INavigable;

            if (_UseINavigable && (oldvm!=null))
            {
                oldvm.Navigation = null;
            }

            var wh = new WindowHelper(new HTMLLogicWindow());

            if (_CurrentWebControl != null)
                _CurrentWebControl.HTMLWindow.Crashed -= Crashed;

            var closetask = ( _CurrentWebControl!=null) ? _Window.CloseAsync() : TaskHelper.Ended();

            _NextWebControl = _WebViewLifeCycleManager.Create();
            _NextWebControl.HTMLWindow.ConsoleMessage += ConsoleMessage;

            var moderWindow = _NextWebControl.HTMLWindow as IModernWebBrowserWindow;
            if (moderWindow!=null)
            {
                var debugContext = _WebViewLifeCycleManager.DebugContext;
                EventHandler<BeforeJavascriptExcecutionArgs> before = null;
                before = (o,e) =>
                {
                    moderWindow.BeforeJavascriptExecuted -= before;
                    e.JavascriptExecutor(_javascriptFrameworkManager.GetMainScript(debugContext));
                };
                moderWindow.BeforeJavascriptExecuted += before;
            }

            var tcs = new TaskCompletionSource<IHTMLBinding>();

            EventHandler<LoadEndEventArgs> sourceupdate = null;
            sourceupdate = (o, e) =>
            {
                var injectorFactory = GetInjectorFactory(uri);
                _NextWebControl.HTMLWindow.LoadEnd -= sourceupdate;
                var engine = new HTMLViewEngine(_NextWebControl, injectorFactory, _webSessionLogger);

                HTML_Binding.Bind(engine, iViewModel, iMode, wh).WaitWith(closetask, t => Switch(t, wh.__window__, tcs));
            };

            Url = uri;
            _NextWebControl.HTMLWindow.LoadEnd += sourceupdate;
            _NextWebControl.HTMLWindow.NavigateTo(uri);

            return tcs.Task;
        }

        private IJavascriptFrameworkManager GetInjectorFactory(Uri iUri)
        {
            return _javascriptFrameworkManager;
        }

        public void ExcecuteJavascript(string icode)
        {
            try 
            {
                _CurrentWebControl?.HTMLWindow.MainFrame.ExecuteJavaScript(icode);
            }
            catch(Exception e)
            {
                _webSessionLogger.Error($"Can not execute javascript: {icode}, reason: {e}");
            }          
        }

        public async Task<IHTMLBinding> NavigateAsync(object iViewModel, string id = null,JavascriptBindingMode iMode = JavascriptBindingMode.TwoWay)
        {
            if ((iViewModel == null) || (_Navigating))
                return null;

            var viewPath = _UrlSolver.Solve(iViewModel, id);
            if (viewPath == null)
                throw ExceptionHelper.Get($"Unable to locate ViewModel {iViewModel}");

            return await Navigate(viewPath, iViewModel, iMode);
        }

        public void Dispose()
        {
            _Disposed = true;
            Binding = null;
            UseINavigable = false;

            CleanWebControl(ref _CurrentWebControl);
            CleanWebControl(ref _NextWebControl);
        }

        private void CleanWebControl(ref IWebBrowserWindowProvider iWebControl)
        {
            if (iWebControl == null)
                return;

            iWebControl.HTMLWindow.Crashed -= Crashed;
            iWebControl.HTMLWindow.ConsoleMessage -= ConsoleMessage;
            iWebControl.Dispose();
            iWebControl = null;
        }

        public event EventHandler<NavigationEvent> OnNavigate;

        public event EventHandler<DisplayEvent> OnDisplay;

        public event EventHandler OnFirstLoad;
    }
}
