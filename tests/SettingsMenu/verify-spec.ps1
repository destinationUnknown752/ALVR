$ErrorActionPreference = 'Stop'


$settingsSourcePath = Join-Path $PSScriptRoot '../../managed/VrSettingsMenu.cs'
$settingsSource = Get-Content -LiteralPath $settingsSourcePath -Raw
$settingsSpec = $settingsSource.Substring(0, $settingsSource.IndexOf('    internal sealed class VrSettingsMenu')) + "`n}"
$settingsSpec = $settingsSpec -replace '(?m)^using .*;\r?\n', ''
$settingsHarness = @'
using System;
using System.Globalization;
using BepInEx.Configuration;
namespace BepInEx.Configuration {
 public class ConfigDefinition { public string Key = "test"; }
 public class ConfigEntryBase {
  public Type SettingType; public object DefaultValue, BoxedValue;
  public ConfigDefinition Definition = new ConfigDefinition();
  public ConfigEntryBase(Type type, object value) { SettingType = type; DefaultValue = BoxedValue = value; }
 }
}
namespace AmanatsuLocationVR {
 public static class SettingsSpecChecks {
  static int checks;
  static void Check(bool result,string label){if(!result)throw new Exception(label);++checks;}
  static bool Close(float a,float b){return Math.Abs(a-b)<0.00001f;}
  public static string Run(){
   var near = new SettingSpec(new ConfigEntryBase(typeof(float),0.15f),"Near clip","View",0.01f,0.5f,0.01f,"m");
   Check(near.NumericValue(0.158f) is float,"float type retained");
   Check(Close((float)near.NumericValue(0.158f),0.16f),"step snapping");
   Check(Close((float)near.NumericValue(-1),0.01f),"lower bound");
   Check(Close((float)near.NumericValue(2),0.5f),"upper bound");
   Check(near.Format(0.15f)=="0.15 m","decimal precision tolerates float boundary");
   var cm = new SettingSpec(new ConfigEntryBase(typeof(float),0.05f),"Near clip","View",0.001f,0.5f,0.001f,"cm",100f);
   Check(cm.Format(0.05f)=="5.0 cm","display units");
   Check(Close((float)cm.NumericValue(0.052f),0.052f),"display scale never changes stored units");
   var offset = new SettingSpec(new ConfigEntryBase(typeof(float),0f),"Offset","POV",-2,2,0.05f);
   Check(Close((float)offset.NumericValue(-0.16f),-0.15f),"negative offset snaps symmetrically");
   Check(Close((float)offset.NumericValue(-5),-2),"negative bound");
   var font = new SettingSpec(new ConfigEntryBase(typeof(int),36),"Font","Subtitles",12,72,1,"px");
   Check(font.NumericValue(36.7f) is int && (int)font.NumericValue(36.7f)==37,"int type and rounding");
   Check(font.Format(36)=="36 px","integer formatting");
   var toggle = new SettingSpec(new ConfigEntryBase(typeof(bool),true),"Hide head","View");
   Check(toggle.IsBoolean&&toggle.Format(true)=="On"&&toggle.Format(false)=="Off","boolean control");
   Check((bool)toggle.Entry.DefaultValue,"default metadata retained");
   bool rejected=false;try{near.NumericValue(float.NaN);}catch(ArgumentOutOfRangeException){rejected=true;}
   Check(rejected,"nonfinite input rejected");
   rejected=false;try{new SettingSpec(new ConfigEntryBase(typeof(float),0f),"Invalid","View",1,0);}catch(ArgumentException){rejected=true;}
   Check(rejected,"inverted bounds rejected");
   rejected=false;try{new SettingSpec(new ConfigEntryBase(typeof(string),"x"),"Invalid","View");}catch(ArgumentException){rejected=true;}
   Check(rejected,"unsupported entry type rejected");
   return "PASS "+checks+" settings control conversion, range, unit and validation checks.";
  }
 }
}
'@
Add-Type -TypeDefinition ($settingsHarness + $settingsSpec)
[AmanatsuLocationVR.SettingsSpecChecks]::Run()
