// Lists the numbers the released jar's matcher finds in a text file, at the valid leniency, as start and length per line. Used to hold
// our matcher to the jar on a long text. Executed against the jar, never read.
//
//   java -cp libphonenumber-<version>.jar FindCount.java <text file> <region> <out file>

import com.google.i18n.phonenumbers.PhoneNumberMatch;
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.PhoneNumberUtil.Leniency;

import java.io.BufferedWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;

public class FindCount {
  public static void main(String[] args) throws Exception {
    String text = new String(Files.readAllBytes(Paths.get(args[0])), StandardCharsets.UTF_8);
    PhoneNumberUtil util = PhoneNumberUtil.getInstance();
    try (BufferedWriter w = Files.newBufferedWriter(Paths.get(args[2]), StandardCharsets.UTF_8)) {
      for (PhoneNumberMatch m : util.findNumbers(text, args[1], Leniency.VALID, Long.MAX_VALUE)) {
        w.write(m.start() + "\t" + m.rawString().length() + "\n");
      }
    }
  }
}
