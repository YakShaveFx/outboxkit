import http from "k6/http";
import { check } from "k6";

export const options = {
  insecureSkipTLSVerify: true,
};

export default function () {
  const result = http.post("http://localhost:6001/produce-something");
  check(result, {
    "status is 200": (r) => r.status === 200,
  });
}
