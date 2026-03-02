Set WshShell = CreateObject("WScript.Shell")
WshShell.CurrentDirectory = "GameEngine.Desktop\bin\Debug\net8.0"
WshShell.Run "GameEngine.Desktop.exe", 1, False
