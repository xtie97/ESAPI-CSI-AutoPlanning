Put the two ESAPI 18 reference assemblies here if Visual Studio cannot find them:

  VMS.TPS.Common.Model.API.dll
  VMS.TPS.Common.Model.Types.dll

Copy them from an Eclipse 18 workstation / TBOX, usually
  C:\Program Files (x86)\Varian\RTM\18.0\esapi\API\   (or 18.1)

PowerShell to locate them on a machine:
  Get-ChildItem 'C:\Program Files (x86)\Varian' -Recurse -Filter VMS.TPS.Common.Model.API.dll -ErrorAction SilentlyContinue

Use the DLLs from the SAME Eclipse version you deploy to. Do not commit or redistribute them (Varian property).
Alternatively set the environment variable ESAPI_DIR, or build with -p:EsapiDir="<folder>".
