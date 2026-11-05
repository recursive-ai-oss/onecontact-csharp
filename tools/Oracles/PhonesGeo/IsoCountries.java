// Probe: prints the ISO 3166-1 alpha-2 codes the JDK running the reference knows, one per line. It is only executed.
public class IsoCountries {
  public static void main(String[] args) {
    for (String c : java.util.Locale.getISOCountries()) System.out.println(c);
  }
}
