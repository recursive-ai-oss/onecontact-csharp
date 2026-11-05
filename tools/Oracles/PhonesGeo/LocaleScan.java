// Probe: which of the given language tags (one per line on stdin) are locales the JDK running the reference has names for, and what
// the JDK reports as the language of each. It is only executed.
//   java LocaleScan.java < tags
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.Arrays;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

public class LocaleScan {
  public static void main(String[] args) throws Exception {
    Set<Locale> available = new HashSet<>(Arrays.asList(Locale.getAvailableLocales()));
    BufferedReader in = new BufferedReader(new InputStreamReader(System.in, "UTF-8"));
    String line;
    while ((line = in.readLine()) != null) {
      Locale l = Locale.forLanguageTag(line.trim());
      System.out.println(line.trim() + "\t" + (available.contains(l) ? "yes" : "no") + "\t" + l.toLanguageTag());
    }
  }
}
