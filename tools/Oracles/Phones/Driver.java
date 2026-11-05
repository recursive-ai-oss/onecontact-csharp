// Parity driver: runs generated cases through a released libphonenumber jar using its public API, and writes one result line per
// (case, operation). The jar is only executed: nothing here reads or links against its sources.
//
//   java -cp libphonenumber-<version>.jar Driver.java <cases.tsv> <results.tsv>
//
// Case lines are tab-separated: id, kind, region, text. Kinds: P (a text to parse with a default region, "-" meaning none),
// R (a region: region-level facts and examples), N (a non-geographic calling code in the text column).
// Text and values escape backslash as \\ and every char outside printable ASCII as a backslash, u and four hex digits.

import com.google.i18n.phonenumbers.NumberParseException;
import com.google.i18n.phonenumbers.PhoneNumberUtil;
import com.google.i18n.phonenumbers.PhoneNumberUtil.PhoneNumberFormat;
import com.google.i18n.phonenumbers.PhoneNumberUtil.PhoneNumberType;
import com.google.i18n.phonenumbers.Phonenumber.PhoneNumber;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public class Driver {
  static final PhoneNumberUtil util = PhoneNumberUtil.getInstance();
  static final String[] OUT_OF_COUNTRY = {"US", "GB", "DE", "AU", "JP", "BR", "MX", "RU", "NZ", "AR", "CA", "KR"};
  static final String[] MOBILE_FROM = {"US", "BR", "MX", "CL", "UZ", "GB", "CA", "CN"};

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
    } catch (NumberParseException e) {
      value = "ERR:" + e.getErrorType().name();
    } catch (Throwable t) {
      value = "EXC:" + t.getClass().getSimpleName();
    }
    w.write(id + "\t" + op + "\t" + value + "\n");
  }

  static String parts(PhoneNumber n, boolean raw) {
    boolean ilz = n.isItalianLeadingZero();
    StringBuilder sb = new StringBuilder();
    sb.append("cc=").append(n.getCountryCode());
    sb.append(" nn=").append(n.getNationalNumber());
    sb.append(" ilz=").append(ilz ? 1 : 0);
    sb.append(" nlz=").append(ilz ? n.getNumberOfLeadingZeros() : 0);
    sb.append(" ext=").append(n.hasExtension() ? esc(n.getExtension()) : "-");
    if (raw) {
      sb.append(" src=").append(n.getCountryCodeSource().name());
      sb.append(" carrier=").append(n.hasPreferredDomesticCarrierCode() ? esc(n.getPreferredDomesticCarrierCode()) : "-");
    }
    return sb.toString();
  }

  static String e164(PhoneNumber n) {
    return n == null ? "-" : util.format(n, PhoneNumberFormat.E164) + (n.hasExtension() ? ";ext=" + esc(n.getExtension()) : "");
  }

  static void phoneCase(BufferedWriter w, String id, String region, String text) throws Exception {
    final String reg = region.equals("-") ? null : region;
    final PhoneNumber[] holder = new PhoneNumber[1];
    emit(w, id, "parsePlain", () -> parts(util.parse(text, reg), false));
    final boolean[] ok = {true};
    emit(w, id, "parse", () -> {
      try {
        holder[0] = util.parseAndKeepRawInput(text, reg);
      } catch (NumberParseException e) {
        ok[0] = false;
        throw e;
      }
      return parts(holder[0], true);
    });
    emit(w, id, "alpha", () -> String.valueOf(util.isAlphaNumber(text)));
    if (!ok[0]) return;
    final PhoneNumber n = holder[0];
    emit(w, id, "valid", () -> String.valueOf(util.isValidNumber(n)));
    emit(w, id, "validFor", () -> reg == null ? "-" : String.valueOf(util.isValidNumberForRegion(n, reg)));
    emit(w, id, "region", () -> esc(util.getRegionCodeForNumber(n)));
    emit(w, id, "type", () -> util.getNumberType(n).name());
    emit(w, id, "possible", () -> String.valueOf(util.isPossibleNumber(n)));
    emit(w, id, "reason", () -> util.isPossibleNumberWithReason(n).name());
    emit(w, id, "reasonByType", () -> {
      StringBuilder sb = new StringBuilder();
      for (PhoneNumberType t : PhoneNumberType.values()) {
        sb.append(t.name()).append('=').append(util.isPossibleNumberForTypeWithReason(n, t).name()).append(' ');
        sb.append(util.isPossibleNumberForType(n, t) ? 'T' : 'F').append(';');
      }
      return sb.toString();
    });
    emit(w, id, "e164", () -> esc(util.format(n, PhoneNumberFormat.E164)));
    emit(w, id, "intl", () -> esc(util.format(n, PhoneNumberFormat.INTERNATIONAL)));
    emit(w, id, "national", () -> esc(util.format(n, PhoneNumberFormat.NATIONAL)));
    emit(w, id, "rfc3966", () -> esc(util.format(n, PhoneNumberFormat.RFC3966)));
    emit(w, id, "ooc", () -> {
      StringBuilder sb = new StringBuilder();
      List<String> from = new ArrayList<>();
      if (reg != null) from.add(reg);
      String own = util.getRegionCodeForNumber(n);
      if (own != null && !own.equals("001")) from.add(own);
      Collections.addAll(from, OUT_OF_COUNTRY);
      for (String f : from) sb.append(f).append('=').append(esc(util.formatOutOfCountryCallingNumber(n, f))).append('|');
      return sb.toString();
    });
    emit(w, id, "mobile", () -> {
      StringBuilder sb = new StringBuilder();
      List<String> from = new ArrayList<>();
      if (reg != null) from.add(reg);
      String own = util.getRegionCodeForNumber(n);
      if (own != null && !own.equals("001")) from.add(own);
      Collections.addAll(from, MOBILE_FROM);
      for (String f : from) {
        sb.append(f).append('=').append(esc(util.formatNumberForMobileDialing(n, f, true)));
        sb.append('/').append(esc(util.formatNumberForMobileDialing(n, f, false))).append('|');
      }
      return sb.toString();
    });
    emit(w, id, "original", () -> {
      StringBuilder sb = new StringBuilder();
      List<String> from = new ArrayList<>();
      if (reg != null) from.add(reg);
      from.add("US");
      from.add("GB");
      from.add("DE");
      for (String f : from) sb.append(f).append('=').append(esc(util.formatInOriginalFormat(n, f))).append('|');
      return sb.toString();
    });
    emit(w, id, "carrier", () -> esc(util.formatNationalNumberWithCarrierCode(n, "99")) + "|" + esc(util.formatNationalNumberWithPreferredCarrierCode(n, "FB")));
    emit(w, id, "intlDial", () -> String.valueOf(util.canBeInternationallyDialled(n)));
    emit(w, id, "geo", () -> String.valueOf(util.isNumberGeographical(n)));
    emit(w, id, "acl", () -> String.valueOf(util.getLengthOfGeographicalAreaCode(n)));
    emit(w, id, "ndc", () -> String.valueOf(util.getLengthOfNationalDestinationCode(n)));
    emit(w, id, "nsn", () -> util.getNationalSignificantNumber(n));
  }

  static void regionCase(BufferedWriter w, String id, String region) throws Exception {
    emit(w, id, "cc", () -> String.valueOf(util.getCountryCodeForRegion(region)));
    emit(w, id, "ndd", () -> esc(util.getNddPrefixForRegion(region, true)) + "|" + esc(util.getNddPrefixForRegion(region, false)));
    emit(w, id, "nanpa", () -> String.valueOf(util.isNANPACountry(region)));
    emit(w, id, "codes", () -> {
      List<String> l = util.getRegionCodesForCountryCode(util.getCountryCodeForRegion(region));
      return String.join(",", l);
    });
    emit(w, id, "exampleRegion", () -> e164(util.getExampleNumber(region)));
    for (PhoneNumberType t : PhoneNumberType.values()) {
      emit(w, id, "example:" + t.name(), () -> e164(util.getExampleNumberForType(region, t)));
    }
    emit(w, id, "supportedTypes", () -> {
      List<String> l = new ArrayList<>();
      for (PhoneNumberType t : util.getSupportedTypesForRegion(region)) l.add(t.name());
      Collections.sort(l);
      return String.join(",", l);
    });
  }

  static void nonGeoCase(BufferedWriter w, String id, int cc) throws Exception {
    emit(w, id, "exampleNonGeo", () -> e164(util.getExampleNumberForNonGeoEntity(cc)));
    emit(w, id, "codesNonGeo", () -> String.join(",", util.getRegionCodesForCountryCode(cc)));
  }

  public static void main(String[] args) throws Exception {
    String version = PhoneNumberUtil.class.getPackage().getImplementationVersion();
    System.err.println("libphonenumber self-reported version: " + version);
    int count = 0;
    try (BufferedReader r = Files.newBufferedReader(Paths.get(args[0]), StandardCharsets.UTF_8);
        BufferedWriter w = Files.newBufferedWriter(Paths.get(args[1]), StandardCharsets.UTF_8)) {
      w.write("#\tversion\t" + esc(version) + "\n");
      String line;
      while ((line = r.readLine()) != null) {
        if (line.isEmpty() || line.startsWith("#")) continue;
        String[] f = line.split("\t", -1);
        String id = f[0];
        switch (f[1]) {
          case "P" -> phoneCase(w, id, f[2], unesc(f[3]));
          case "R" -> regionCase(w, id, f[2]);
          case "N" -> nonGeoCase(w, id, Integer.parseInt(f[3]));
          default -> throw new IllegalArgumentException("kind " + f[1]);
        }
        count++;
      }
    }
    System.err.println("cases: " + count);
  }
}
