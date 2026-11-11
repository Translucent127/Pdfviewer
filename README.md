# PDF Viewer

A lightweight C#/WPF desktop application built for fast PDF document inspection, automated text copying, and real-time Stock Keeping Unit (SKU) summary management.

---

🌟 Key Features

* **Native & Secure PDF Viewer**
  * Powered by Microsoft Edge (`WebView2`) for fast and reliable PDF rendering.
  * **Navigation Guard:** Automatically blocks external web links (e.g., tracking URLs like `http://` or `https://`) to prevent accidental web redirection within the PDF viewport.

* **Automated Text Copying (Auto Copy)**
  * Uses a Win32 Low-Level Mouse Hook to automatically copy text highlighted inside the PDF viewer directly to the clipboard.
  * Interactive toast pop-up notification confirms every successful copy action.

* **Interactive SKU Summary Panel**
  * **Flexible Input:** Add SKUs manually via keyboard input or adjust quantities using dedicated `+` / `-` buttons or direct text box editing.
  * **Duplicate Detection & Re-ordering:** Re-entering an existing SKU moves it directly to the top of the list without incrementing its count.
  * **Visual Flashing Alert:** Moved duplicate SKUs trigger a 3-time rapid red flash animation for immediate visual confirmation.
  * **One-Click Export:** Copies all active SKUs (sorted by highest quantity first) to the clipboard in a clean summary format.
  * **In-App Clear Confirmation:** Features an in-app dark overlay pop-up dialog to confirm before clearing all entered SKUs.

* **Minimalist Dark Theme UI**
  * Sleek, modern dark-themed user interface with consistent button dimensions and layout.
  * **Persistent Settings:** Automatically saves and restores window dimensions, position, and screen state to local configuration files upon closing.

---

🛠️ Tech Stack

* **Framework:** C# / .NET (WPF)
* **Rendering Engine:** Microsoft WebView2 (`Microsoft.Web.WebView2`)
* **System Interop:** Win32 P/Invoke (`user32.dll`, `kernel32.dll` for Mouse Hooking & Keyboard Input Emulation)
* **Configuration:** JSON File Storage (`AppData`)

---

🚀 Getting Started

Prerequisites

* Windows 10 / 11
* [.NET Desktop Development Workload](https://dotnet.microsoft.com/)
* [Visual Studio 2022](https://visualstudio.microsoft.com/) (or .NET CLI)
* Microsoft Edge WebView2 Runtime (pre-installed on most modern Windows systems)

Installation & Build

1. **Clone the Repository**
   ```bash
   git clone [https://github.com/YOUR-USERNAME/PDF-Viewer-Pro.git](https://github.com/YOUR-USERNAME/PDF-Viewer-Pro.git)
   cd PDF-Viewer-Pro
   
