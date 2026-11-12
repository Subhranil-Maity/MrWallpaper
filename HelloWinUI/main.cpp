// Minimal WinUI 3 "Hello World" built without the Windows App SDK NuGet package.
// It uses the Windows App Runtime framework package that is already installed,
// added to the process at startup with the Windows 11 Dynamic Dependencies API.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <appmodel.h>
#undef GetCurrentTime  // windows.h macro clashes with a WinUI method name

#include <winrt/Windows.Foundation.h>
#include <winrt/Microsoft.UI.Xaml.h>
#include <winrt/Microsoft.UI.Xaml.Controls.h>
#include <winrt/Microsoft.UI.Xaml.Media.h>

using namespace winrt;
using namespace winrt::Microsoft::UI::Xaml;
using namespace winrt::Microsoft::UI::Xaml::Controls;

static constexpr wchar_t kRuntimeFamily[] = L"Microsoft.WindowsAppRuntime.1.8_8wekyb3d8bbwe";

struct App : ApplicationT<App>
{
    void OnLaunched(LaunchActivatedEventArgs const&)
    {
        TextBlock text;
        text.Text(L"Hello, World!");
        text.FontSize(48);
        text.HorizontalAlignment(HorizontalAlignment::Center);
        text.VerticalAlignment(VerticalAlignment::Center);

        m_window = Window();
        m_window.Title(L"Hello WinUI 3");
        m_window.Content(text);
        m_window.Activate();
    }

    Window m_window{ nullptr };
};

// Puts the installed Windows App Runtime framework package into this process's
// package graph so WinUI's DLLs and WinRT classes can be found.
static bool AddWindowsAppRuntime()
{
    using TryCreateFn = HRESULT(WINAPI*)(PSID, PCWSTR, PACKAGE_VERSION, PackageDependencyProcessorArchitectures,
                                         PackageDependencyLifetimeKind, PCWSTR, CreatePackageDependencyOptions, PWSTR*);
    using AddFn = HRESULT(WINAPI*)(PCWSTR, INT32, AddPackageDependencyOptions, PACKAGEDEPENDENCY_CONTEXT*, PWSTR*);

    HMODULE kb = GetModuleHandleW(L"kernelbase.dll");
    auto tryCreate = reinterpret_cast<TryCreateFn>(GetProcAddress(kb, "TryCreatePackageDependency"));
    auto add = reinterpret_cast<AddFn>(GetProcAddress(kb, "AddPackageDependency"));
    if (!tryCreate || !add) return false;  // needs Windows 11

    PACKAGE_VERSION minVersion{};
    PWSTR depId = nullptr;
    if (FAILED(tryCreate(nullptr, kRuntimeFamily, minVersion, PackageDependencyProcessorArchitectures_X64,
                         PackageDependencyLifetimeKind_Process, nullptr, CreatePackageDependencyOptions_None, &depId)))
        return false;

    PACKAGEDEPENDENCY_CONTEXT ctx{};
    PWSTR fullName = nullptr;
    HRESULT hr = add(depId, 0, AddPackageDependencyOptions_None, &ctx, &fullName);
    HeapFree(GetProcessHeap(), 0, depId);
    HeapFree(GetProcessHeap(), 0, fullName);
    return SUCCEEDED(hr);
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    if (!AddWindowsAppRuntime())
    {
        MessageBoxW(nullptr, L"Could not load the Windows App Runtime 1.8.", L"Hello WinUI 3", MB_ICONERROR);
        return 1;
    }

    init_apartment(apartment_type::single_threaded);
    Application::Start([](auto&&) { make<App>(); });
    return 0;
}
