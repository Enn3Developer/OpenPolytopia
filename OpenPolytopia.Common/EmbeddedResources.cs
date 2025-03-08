namespace OpenPolytopia.Common;

using System.Reflection;

public static class EmbeddedResources {
  public static string GetTroopsData() {
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream("OpenPolytopia.Common.resources.troops.json");
    if (stream == null) {
      throw new FileNotFoundException("Troops data not found");
    }

    using var streamReader = new StreamReader(stream);
    return streamReader.ReadToEnd();
  }
}
