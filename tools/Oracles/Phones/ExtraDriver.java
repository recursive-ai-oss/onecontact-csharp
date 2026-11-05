// Parity driver for as-you-type formatting, the matcher and short numbers, using the released jar's public API only (it is
// executed, never read). Same file formats as Driver.java.
//
//   java -cp libphonenumber-<version>.jar ExtraDriver.java <cases.tsv> <results.tsv>
//
// Case lines: id, kind, region, text. Kinds:
//   A  as-you-type: the text is the key presses, typed one at a time into a formatter for the region.
//   M  matcher: the text is searched with the region as the default, at every leniency.
//   S  short number: the text is the national digits; the number is +<calling code of the region><digits>.
//   E  emergency: the text is dialled from the region.
//   The jar exposes neither example short numbers nor the list of regions with short-number data publicly; those are tested
//   against the data file directly.

import com.google.i18n.phonenumbers.AsYouTypeFormatter;
import com.google.i18n.phonenumbers.PhoneNumberMatch;
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.PhoneNumberUtil.Leniency;
import com.google.i18n.phonenumbers.Phonenumber.PhoneNumber;
import com.google.i18n.phonenumbers.ShortNumberInfo;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public class ExtraDriver {
  static final PhoneNumberUtil util = PhoneNumberUtil.getInstance();
  static final ShortNumberInfo info = ShortNumberInfo.getInstance();

  interface Op {
    String run() throws Exception;
  }

  static String esc(String s) {
    if (s == null) return "-";
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
        char n = s.charAt(i + 1);
        if (n == '\\') {
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

  static void emit(BufferedWriter w, String id, String op, Op body) throws Exception {
    String value;
    try {
      value = body.run();
    } catch (Throwable t) {
      value = "EXC:" + t.getClass().getSimpleName();
    }
    w.write(id + "\t" + op + "\t" + value + "\n");
  }

  static String parts(PhoneNumber n) {
    return "cc=" + n.getCountryCode() + " nn=" + n.getNationalNumber() + " ilz=" + (n.isItalianLeadingZero() ? 1 : 0)
        + " nlz=" + (n.isItalianLeadingZero() ? n.getNumberOfLeadingZeros() : 0) + " ext=" + (n.hasExtension() ? esc(n.getExtension()) : "-");
  }

  static void asYouType(BufferedWriter w, String id, String region, String text) throws Exception {
    emit(w, id, "ayt", () -> {
      AsYouTypeFormatter f = util.getAsYouTypeFormatter(region);
      StringBuilder sb = new StringBuilder();
      for (int i = 0; i < text.length(); i++) sb.append(esc(f.inputDigit(text.charAt(i)))).append('|');
      return sb.toString();
    });
    emit(w, id, "ayt.rem", () -> {
      AsYouTypeFormatter f = util.getAsYouTypeFormatter(region);
      int k = text.length() / 2;
      StringBuilder sb = new StringBuilder();
      StringBuilder pos = new StringBuilder();
      for (int i = 0; i < text.length(); i++) {
        sb.append(esc(i == k ? f.inputDigitAndRememberPosition(text.charAt(i)) : f.inputDigit(text.charAt(i)))).append('|');
        pos.append(f.getRememberedPosition()).append(',');
      }
      return sb + "@" + pos;
    });
    emit(w, id, "ayt.clear", () -> {
      AsYouTypeFormatter f = util.getAsYouTypeFormatter(region);
      for (int i = 0; i < text.length(); i++) f.inputDigit(text.charAt(i));
      f.clear();
      StringBuilder sb = new StringBuilder();
      for (int i = 0; i < text.length(); i++) sb.append(esc(f.inputDigit(text.charAt(i)))).append('|');
      return sb.toString();
    });
  }

  static String matches(String text, String region, Leniency len, long tries) {
    StringBuilder sb = new StringBuilder();
    for (PhoneNumberMatch m : util.findNumbers(text, region, len, tries)) {
      sb.append(m.start()).append('-').append(m.end()).append(':').append(esc(m.rawString())).append(':').append(parts(m.number())).append('|');
    }
    return sb.toString();
  }

  static void matcher(BufferedWriter w, String id, String region, String text) throws Exception {
    for (Leniency len : Leniency.values()) {
      emit(w, id, "m." + len.name(), () -> matches(text, region, len, Long.MAX_VALUE));
    }
    emit(w, id, "m.VALID.t1", () -> matches(text, region, Leniency.VALID, 1));
    emit(w, id, "m.POSSIBLE.t3", () -> matches(text, region, Leniency.POSSIBLE, 3));
    emit(w, id, "m.EXACT_GROUPING.t2", () -> matches(text, region, Leniency.EXACT_GROUPING, 2));
  }

  static String bool(boolean b) {
    return b ? "1" : "0";
  }

  static void shortNumber(BufferedWriter w, String id, String region, String digits) throws Exception {
    PhoneNumber n;
    try {
      n = util.parse("+" + util.getCountryCodeForRegion(region) + digits, "ZZ");
    } catch (Exception e) {
      emit(w, id, "short.parse", () -> "ERR");
      return;
    }
    emit(w, id, "short.parse", () -> parts(n));
    emit(w, id, "short.possible", () -> bool(info.isPossibleShortNumber(n)));
    emit(w, id, "short.possible.region", () -> bool(info.isPossibleShortNumberForRegion(n, region)));
    emit(w, id, "short.valid", () -> bool(info.isValidShortNumber(n)));
    emit(w, id, "short.valid.region", () -> bool(info.isValidShortNumberForRegion(n, region)));
    emit(w, id, "short.cost", () -> info.getExpectedCost(n).name());
    emit(w, id, "short.cost.region", () -> info.getExpectedCostForRegion(n, region).name());
    emit(w, id, "short.carrier", () -> bool(info.isCarrierSpecific(n)));
    emit(w, id, "short.carrier.region", () -> bool(info.isCarrierSpecificForRegion(n, region)));
    emit(w, id, "short.sms.region", () -> bool(info.isSmsServiceForRegion(n, region)));
  }

  static void emergency(BufferedWriter w, String id, String region, String text) throws Exception {
    emit(w, id, "emerg.connects", () -> bool(info.connectsToEmergencyNumber(text, region)));
    emit(w, id, "emerg.is", () -> bool(info.isEmergencyNumber(text, region)));
  }

  public static void main(String[] args) throws Exception {
    try (BufferedReader r = Files.newBufferedReader(Paths.get(args[0]), StandardCharsets.UTF_8);
        BufferedWriter w = Files.newBufferedWriter(Paths.get(args[1]), StandardCharsets.UTF_8)) {
      String line;
      while ((line = r.readLine()) != null) {
        if (line.isEmpty() || line.charAt(0) == '#') continue;
        String[] f = line.split("\t", -1);
        String id = f[0];
        switch (f[1]) {
          case "A" -> asYouType(w, id, f[2], unesc(f[3]));
          case "M" -> matcher(w, id, f[2], unesc(f[3]));
          case "S" -> shortNumber(w, id, f[2], unesc(f[3]));
          case "E" -> emergency(w, id, f[2], unesc(f[3]));
          default -> throw new IllegalArgumentException(f[1]);
        }
      }
    }
  }
}
