// Parity driver for the geocoder, carrier and time-zone mappers of released libphonenumber jars: answers generated cases with the
// public API only. The jars are only executed: nothing here reads or links against their sources.
//
//   java -cp libphonenumber.jar:geocoder.jar:carrier.jar:prefixmapper.jar GeoDriver.java <cases.tsv> <results.tsv>
//
// A case line is tab separated: id, calling code, national number, language tag, user region ("-" for none). The tag is escaped like the
// results: backslash as \\ and every char outside printable ASCII as backslash, u and four hex digits. A result line is the id and nine
// fields: geocoder (checked, with user region, unchecked, unchecked with user region), carrier (checked, unchecked, safe display name),
// time zones (for a number, for a geographical number; joined with &).
import com.google.i18n.phonenumbers.PhoneNumberToCarrierMapper;
import com.google.i18n.phonenumbers.PhoneNumberToTimeZonesMapper;
import com.google.i18n.phonenumbers.Phonenumber.PhoneNumber;
import com.google.i18n.phonenumbers.geocoding.PhoneNumberOfflineGeocoder;
import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.Locale;

public class GeoDriver {
  static String esc(String s) {
    if (s == null) return "<null>";
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < s.length(); i++) {
      char c = s.charAt(i);
      if (c == '\\') sb.append("\\\\");
      else if (c >= 0x20 && c <= 0x7e) sb.append(c);
      else sb.append(String.format("\\u%04X", (int) c));
    }
    return sb.toString();
  }

  static String unesc(String s) {
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < s.length(); i++) {
      char c = s.charAt(i);
      if (c == '\\') {
        if (s.charAt(i + 1) == '\\') {
          sb.append('\\');
          i++;
        } else {
          sb.append((char) Integer.parseInt(s.substring(i + 2, i + 6), 16));
          i += 5;
        }
      } else {
        sb.append(c);
      }
    }
    return sb.toString();
  }

  public static void main(String[] args) throws Exception {
    PhoneNumberOfflineGeocoder geo = PhoneNumberOfflineGeocoder.getInstance();
    PhoneNumberToCarrierMapper carrier = PhoneNumberToCarrierMapper.getInstance();
    PhoneNumberToTimeZonesMapper tz = PhoneNumberToTimeZonesMapper.getInstance();
    try (BufferedReader in = Files.newBufferedReader(Paths.get(args[0]), StandardCharsets.UTF_8);
        BufferedWriter out = Files.newBufferedWriter(Paths.get(args[1]), StandardCharsets.UTF_8)) {
      String line;
      while ((line = in.readLine()) != null) {
        String[] f = line.split("\t", -1);
        String nsn = f[2];
        int zeros = 0;
        while (zeros < nsn.length() - 1 && nsn.charAt(zeros) == '0') zeros++;
        PhoneNumber n = new PhoneNumber();
        n.setCountryCode(Integer.parseInt(f[1]));
        n.setNationalNumber(Long.parseLong(nsn));
        if (zeros > 0) {
          n.setItalianLeadingZero(true);
          if (zeros > 1) n.setNumberOfLeadingZeros(zeros);
        }
        Locale loc = Locale.forLanguageTag(unesc(f[3]));
        boolean user = !f[4].equals("-");
        StringBuilder sb = new StringBuilder(f[0]);
        sb.append('\t').append(esc(geo.getDescriptionForNumber(n, loc)));
        sb.append('\t').append(user ? esc(geo.getDescriptionForNumber(n, loc, f[4])) : "-");
        sb.append('\t').append(esc(geo.getDescriptionForValidNumber(n, loc)));
        sb.append('\t').append(user ? esc(geo.getDescriptionForValidNumber(n, loc, f[4])) : "-");
        sb.append('\t').append(esc(carrier.getNameForNumber(n, loc)));
        sb.append('\t').append(esc(carrier.getNameForValidNumber(n, loc)));
        sb.append('\t').append(esc(carrier.getSafeDisplayName(n, loc)));
        sb.append('\t').append(esc(String.join("&", tz.getTimeZonesForNumber(n))));
        sb.append('\t').append(esc(String.join("&", tz.getTimeZonesForGeographicalNumber(n))));
        out.write(sb.toString());
        out.write('\n');
      }
    }
  }
}
